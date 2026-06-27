namespace Liakont.Modules.Pipeline.Infrastructure;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Remplit l'identité de l'émetteur (SIREN, raison sociale, adresse) et la nature d'opération d'un pivot
/// depuis le profil du tenant (ADR-0031 amendé). Appliqué au READ-TIME — CHECK et SEND, au chargement du
/// pivot stagé — JAMAIS à l'ingestion (RB9) : l'anti-doublon F06 hashe le pivot SOURCE (ce que l'agent a
/// extrait), donc l'identité émetteur — donnée PLATEFORME, mutable avec le profil — ne doit PAS entrer
/// dans l'empreinte (sinon un changement de profil entre deux extractions de la même source casserait
/// l'idempotence et produirait une fausse « altération »). L'émetteur est ainsi TOUJOURS résolu au profil
/// COURANT au moment du traitement.
/// <para>
/// Remplissage « QUAND ABSENT » : un document portant déjà un émetteur (ex. 389 autofacturation, où le
/// vendeur est le MANDANT) n'est jamais écrasé. Profil tenant ou paramétrage fiscal incomplet → champ
/// laissé <c>null</c> → document bloqué au CHECK (jamais deviné, CLAUDE.md n°2/n°3). <see cref="Enrich"/>
/// est PURE (testable) ; <see cref="EnrichFromTenantAsync"/> lit le paramétrage tenant puis l'applique.
/// </para>
/// </summary>
internal static class PivotEmitterEnricher
{
    // Codes sujet UNTDID 4451 (EN 16931 BT-21) des mentions légales FR obligatoires entre professionnels
    // (BR-FR-05), imposés par le converter CTC-FR (verdict PA) — F16 §3.5, F12-A §3.4. Aucun contenu inventé :
    // seul le mapping mention → code est figé ici ; le TEXTE est saisi par le tenant.
    internal const string LatePenaltySubjectCode = "PMD"; // pénalités de retard
    internal const string RecoveryFeeSubjectCode = "PMT"; // indemnité forfaitaire de recouvrement
    internal const string DiscountSubjectCode = "AAB"; // escompte ou son absence

    /// <summary>Lit le profil + le paramétrage fiscal du tenant (courant) puis applique l'enrichissement.</summary>
    public static async Task<PivotDocumentDto> EnrichFromTenantAsync(
        PivotDocumentDto pivot,
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(tenantSettings);

        var profile = await tenantSettings.GetTenantProfile(companyId, cancellationToken);
        var fiscal = await tenantSettings.GetFiscalSettings(companyId, cancellationToken);
        var mentions = await tenantSettings.GetBillingMentions(companyId, cancellationToken);
        return Enrich(pivot, profile, fiscal, mentions);
    }

    /// <summary>
    /// Renvoie le pivot avec l'émetteur, la nature d'opération et les mentions de facturation (BT-20 +
    /// notes légales FR, BUG-26 / F12-A §3.4) remplis depuis le profil/fiscal/mentions du tenant quand le
    /// document ne les porte pas déjà (la valeur portée par la source PRIME — surcharge par document).
    /// Renvoie le document inchangé si rien n'est à remplir. <paramref name="mentions"/> est optionnel
    /// (additif) : un appelant qui ne le fournit pas n'injecte aucune mention.
    /// </summary>
    public static PivotDocumentDto Enrich(
        PivotDocumentDto document,
        TenantProfileDto? profile,
        FiscalSettingsDto? fiscal,
        BillingMentionsDto? mentions = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var supplier = document.Supplier ?? BuildSupplier(profile);
        var operationCategory = document.OperationCategory ?? ParseOperationCategory(fiscal);

        // Mentions de facturation : défaut tenant injecté UNIQUEMENT quand le document ne les porte pas (la
        // source prime — surcharge par document, F12-A §3.4). BT-20 = termes de paiement ; Notes = mentions
        // légales FR (PMD/PMT/AAB). Le contenu vient du tenant, jamais inventé (CLAUDE.md n°2).
        var paymentTerms = document.PaymentTerms ?? NullIfBlank(mentions?.PaymentTerms);
        var notes = document.Notes ?? BuildTenantNotes(mentions);

        if (ReferenceEquals(supplier, document.Supplier)
            && operationCategory == document.OperationCategory
            && ReferenceEquals(paymentTerms, document.PaymentTerms)
            && ReferenceEquals(notes, document.Notes))
        {
            // Rien à remplir (déjà porté par la source, ou paramétrage tenant incomplet → reste null) : pas
            // de reconstruction inutile (préserve l'identité de l'objet pour le court-circuit des appelants).
            return document;
        }

        return Rebuild(document, supplier, operationCategory, paymentTerms, notes);
    }

    private static PivotPartyDto? BuildSupplier(TenantProfileDto? profile)
    {
        // Profil absent ou sans SIREN → émetteur laissé null : bloqué au CHECK, jamais inventé.
        if (profile is null || string.IsNullOrWhiteSpace(profile.Siren))
        {
            return null;
        }

        return new PivotPartyDto(
            name: profile.RaisonSociale,
            siren: profile.Siren,
            siret: null,
            vatNumber: BuildFrenchIntracomVat(profile.Siren, profile.Country),
            address: new PivotAddressDto(
                line1: NullIfBlank(profile.Street),
                line2: null,
                postalCode: NullIfBlank(profile.PostalCode),
                city: NullIfBlank(profile.City),
                countryCode: NullIfBlank(profile.Country)),
            email: null,
            isCompanyHint: true);
    }

    private static OperationCategory? ParseOperationCategory(FiscalSettingsDto? fiscal)
    {
        // Nature d'opération paramétrée par tenant (chaîne = NOM de l'enum). Parse PAR NOM : les enums
        // OperationCategory du contrat et de TenantSettings ont des valeurs numériques DIFFÉRENTES (jamais
        // de cast numérique). Absente/inconnue → null : bloqué au CHECK (jamais devinée, CLAUDE.md n°2).
        if (fiscal is null || string.IsNullOrWhiteSpace(fiscal.OperationCategory))
        {
            return null;
        }

        return Enum.TryParse<OperationCategory>(fiscal.OperationCategory, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // Construit les notes de document (BG-1) à partir des mentions tenant : une note par mention renseignée,
    // avec son code sujet. Mention vide → note OMISE (rien à émettre). Aucune mention → null (hash-neutre).
    private static List<PivotDocumentNoteDto>? BuildTenantNotes(BillingMentionsDto? mentions)
    {
        if (mentions is null)
        {
            return null;
        }

        var notes = new List<PivotDocumentNoteDto>(3);
        AddNote(notes, mentions.LatePenaltyTerms, LatePenaltySubjectCode);
        AddNote(notes, mentions.RecoveryFeeTerms, RecoveryFeeSubjectCode);
        AddNote(notes, mentions.DiscountTerms, DiscountSubjectCode);
        return notes.Count > 0 ? notes : null;
    }

    private static void AddNote(List<PivotDocumentNoteDto> notes, string? content, string subjectCode)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            notes.Add(new PivotDocumentNoteDto(content!, subjectCode));
        }
    }

    /// <summary>
    /// N° TVA intracommunautaire FR (EN 16931 BT-31) dérivé du SIREN de l'émetteur : <c>"FR"</c> + clé de
    /// contrôle + SIREN. La clé est la formule administrative STANDARD française
    /// (<c>(12 + 3 × (SIREN mod 97)) mod 97</c>) — déterministe et publique, ce n'est pas une règle fiscale
    /// inventée (CLAUDE.md n°2). Dérivée UNIQUEMENT pour un émetteur français (<c>country == "FR"</c>) dont
    /// le SIREN est bien formé (9 chiffres) ; sinon <c>null</c> (le BT-31 reste absent — jamais deviné).
    /// Requis car la conversion EN 16931 (BR-S-02/…) exige le n° TVA vendeur dès qu'une ligne porte de la TVA.
    /// </summary>
    private static string? BuildFrenchIntracomVat(string? siren, string? country)
    {
        if (!string.Equals(country, "FR", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(siren) || siren.Length != 9
            || !long.TryParse(siren, NumberStyles.None, CultureInfo.InvariantCulture, out var numericSiren))
        {
            return null;
        }

        var key = (12 + (3 * (numericSiren % 97))) % 97;
        return string.Create(CultureInfo.InvariantCulture, $"FR{key:D2}{siren}");
    }

    private static PivotDocumentDto Rebuild(
        PivotDocumentDto pivot,
        PivotPartyDto? supplier,
        OperationCategory? operationCategory,
        string? paymentTerms,
        IReadOnlyList<PivotDocumentNoteDto>? notes) =>
        new(
            sourceDocumentKind: pivot.SourceDocumentKind,
            number: pivot.Number,
            issueDate: pivot.IssueDate,
            sourceReference: pivot.SourceReference,
            supplier: supplier,
            totals: pivot.Totals,
            operationCategory: operationCategory,
            currencyCode: pivot.CurrencyCode,
            customer: pivot.Customer,
            lines: pivot.Lines,
            creditNoteRefs: pivot.CreditNoteRefs,
            payments: pivot.Payments,
            documentCharges: pivot.DocumentCharges,
            invoicer: pivot.Invoicer,
            payee: pivot.Payee,
            isSelfBilled: pivot.IsSelfBilled,
            prepaidAmount: pivot.PrepaidAmount,
            sourceData: pivot.SourceData,
            paymentDueDate: pivot.PaymentDueDate,
            isB2cReportingDeclaration: pivot.IsB2cReportingDeclaration,
            sellerFees: pivot.SellerFees,
            buyerFees: pivot.BuyerFees,
            invoicePeriod: pivot.InvoicePeriod,
            paymentTerms: paymentTerms,
            notes: notes,
            deliveryDate: pivot.DeliveryDate);
}
