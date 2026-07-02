namespace Liakont.Modules.Pipeline.Infrastructure;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Reference.Contracts;

/// <summary>
/// Normalise le code pays de l'ACHETEUR d'un pivot (<c>Customer.Address.CountryCode</c>) vers ISO 3166-1
/// alpha-2 via le référentiel de correspondance (ADR-0038, <see cref="ICountryAliasReferential"/>), au
/// READ-TIME plateforme (CHECK / SEND / affichage) — miroir de <see cref="PivotEmitterEnricher"/>. JAMAIS à
/// l'ingestion : l'anti-doublon F06 hashe le pivot SOURCE, donc normaliser avant le hash ferait diverger
/// l'empreinte à chaque édition du référentiel (INV-REF-CTRY-02). NULL-SAFE (acheteur / adresse / pays absent
/// → document inchangé) et IDEMPOTENT (un code déjà ISO ou non mappé est renvoyé tel quel → aucune
/// reconstruction). Un code non mappé reste BRUT → bloqué en aval par BT-55 (fail-closed, INV-REF-CTRY-03) :
/// ce normaliseur n'affaiblit JAMAIS la validation.
/// <para>
/// Portée V1 : seul <c>Customer</c> porte un pays SOURCE (toutes les parties EncheresV6 y atterrissent) ;
/// <c>Supplier</c> / <c>Payee</c> sont remplis par la plateforme (déjà ISO). Si un futur producteur (389 /
/// F15) remplit <c>Supplier</c> avec un pays source, ÉTENDRE ce normaliseur à cette partie.
/// </para>
/// </summary>
internal static class PivotCountryNormalizer
{
    /// <summary>
    /// Renvoie le pivot avec le code pays de l'acheteur normalisé ISO, ou le document INCHANGÉ (même instance)
    /// s'il n'y a rien à normaliser (pas d'acheteur / adresse / pays, ou code déjà ISO / non mappé).
    /// </summary>
    public static async Task<PivotDocumentDto> NormalizeAsync(
        PivotDocumentDto pivot,
        ICountryAliasReferential referential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(referential);

        var normalizedCustomer = await NormalizePartyCountryAsync(pivot.Customer, referential, cancellationToken);

        // Court-circuit : identité d'objet préservée quand rien n'a changé (pas d'acheteur / adresse / pays, ou
        // code déjà ISO / non mappé) — aucune reconstruction inutile, cohérent avec PivotEmitterEnricher.
        return ReferenceEquals(normalizedCustomer, pivot.Customer)
            ? pivot
            : Rebuild(pivot, normalizedCustomer);
    }

    private static async Task<PivotPartyDto?> NormalizePartyCountryAsync(
        PivotPartyDto? party, ICountryAliasReferential referential, CancellationToken cancellationToken)
    {
        var address = party?.Address;
        var rawCountry = address?.CountryCode;
        if (address is null || string.IsNullOrWhiteSpace(rawCountry))
        {
            return party;
        }

        var normalized = await referential.ResolveAsync(rawCountry, cancellationToken);
        if (string.Equals(normalized, rawCountry, StringComparison.Ordinal))
        {
            // Idempotent : code déjà ISO ou non mappé (fail-closed) → inchangé, aucune reconstruction.
            return party;
        }

        var normalizedAddress = new PivotAddressDto(
            line1: address.Line1,
            line2: address.Line2,
            postalCode: address.PostalCode,
            city: address.City,
            countryCode: normalized);

        return new PivotPartyDto(
            name: party!.Name,
            siren: party.Siren,
            siret: party.Siret,
            vatNumber: party.VatNumber,
            address: normalizedAddress,
            email: party.Email,
            isCompanyHint: party.IsCompanyHint);
    }

    private static PivotDocumentDto Rebuild(PivotDocumentDto pivot, PivotPartyDto? customer) =>
        new(
            sourceDocumentKind: pivot.SourceDocumentKind,
            number: pivot.Number,
            issueDate: pivot.IssueDate,
            sourceReference: pivot.SourceReference,
            supplier: pivot.Supplier,
            totals: pivot.Totals,
            operationCategory: pivot.OperationCategory,
            currencyCode: pivot.CurrencyCode,
            customer: customer,
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
            paymentTerms: pivot.PaymentTerms,
            notes: pivot.Notes,
            deliveryDate: pivot.DeliveryDate);
}
