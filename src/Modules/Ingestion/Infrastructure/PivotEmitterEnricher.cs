namespace Liakont.Modules.Ingestion.Infrastructure;

using System;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Remplit l'identité de l'émetteur (SIREN, raison sociale, adresse) et la nature d'opération d'un pivot
/// reçu d'un agent DEPUIS le profil du tenant (ADR-0023 amendé) : l'agent n'extrait que la base source et
/// ne porte JAMAIS le SIREN émetteur (F01-F02 §4.3) — il est l'identité du TENANT, paramétrée côté
/// plateforme. Remplissage « QUAND ABSENT » : un document qui porte déjà un émetteur (ex. 389
/// autofacturation sous mandat, où le vendeur est le MANDANT, pas le tenant) n'est jamais écrasé. Si le
/// profil tenant ou le paramétrage fiscal est incomplet, le champ reste <c>null</c> → le document est
/// bloqué au CHECK (SUPPLIER_SIREN_MISSING / nature d'opération manquante), jamais deviné (CLAUDE.md
/// n°2/n°3). Fonction PURE (aucune I/O) — testable en isolation.
/// </summary>
internal static class PivotEmitterEnricher
{
    /// <summary>
    /// Renvoie le pivot avec l'émetteur et la nature d'opération remplis depuis le profil/paramétrage
    /// fiscal du tenant quand le document ne les porte pas déjà. Renvoie le document inchangé si rien
    /// n'est à remplir.
    /// </summary>
    public static PivotDocumentDto Enrich(PivotDocumentDto document, TenantProfileDto? profile, FiscalSettingsDto? fiscal)
    {
        ArgumentNullException.ThrowIfNull(document);

        var supplier = document.Supplier ?? BuildSupplier(profile);
        var operationCategory = document.OperationCategory ?? ParseOperationCategory(fiscal);

        if (ReferenceEquals(supplier, document.Supplier) && operationCategory == document.OperationCategory)
        {
            // Rien à remplir (déjà porté par la source, ou profil/fiscal incomplet → reste null) : pas de
            // reconstruction inutile (préserve l'empreinte canonique d'un document déjà complet).
            return document;
        }

        return Rebuild(document, supplier, operationCategory);
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
            vatNumber: null,
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

    private static PivotDocumentDto Rebuild(PivotDocumentDto pivot, PivotPartyDto? supplier, OperationCategory? operationCategory) =>
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
            paymentDueDate: pivot.PaymentDueDate);
}
