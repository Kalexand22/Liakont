namespace Liakont.PaClients.B2Brouter;

using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Transforme le document PIVOT enrichi (EN 16931, mapping TVA déjà appliqué par la plateforme —
/// cf. <see cref="Modules.Transmission.Contracts.IPaClient"/>) vers le payload propriétaire B2Brouter
/// (F05 §2 ; F03 §2.3 modèle « 2 lignes marge »). Le plug-in NE CALCULE RIEN et N'INVENTE AUCUNE
/// règle fiscale (CLAUDE.md n°1/2) : il recopie les montants (en <see cref="decimal"/>) et propage
/// la catégorie UNCL5305 / le taux / le code VATEX déjà portés par le pivot. La construction du
/// payload PA-spécifique vit DANS le plug-in (F05 §6 amendé pivot ; ajouter-un-plugin-pa §1).
/// </summary>
internal static class B2BrouterPayloadBuilder
{
    // En V1, Liakont est B2C : la facture/avoir part en facture simplifiée émise (F07-F08 :
    // « on envoie l'avoir comme IssuedSimplifiedInvoice avec is_credit_note: true »). Le B2B
    // (SupportsB2bInvoicing) est une capacité de phase 2 — non couverte ici (PAB03 §5).
    private const string SimplifiedInvoiceType = "IssuedSimplifiedInvoice";

    /// <summary>
    /// Construit l'enveloppe d'import B2Brouter pour un document pivot. Un avoir (porteur d'au moins
    /// une <see cref="PivotDocumentDto.CreditNoteRefs"/>) renseigne <c>is_credit_note</c> /
    /// <c>is_amend</c> / <c>amended_number</c> / <c>amended_date</c> à partir de la première référence
    /// d'origine (F05). La décision « facture vs avoir » n'est PAS prise ici : elle est portée par le
    /// pivot (présence de références d'origine — la classification vit dans Validation, ADR-0004 D3-3).
    /// </summary>
    /// <param name="document">Le document pivot enrichi à transmettre.</param>
    /// <param name="sendAfterImport">Vrai = créer ET envoyer ; faux = créé sans envoi (F05 §2).</param>
    public static B2BrouterInvoiceRequest Build(PivotDocumentDto document, bool sendAfterImport)
    {
        var isCreditNote = document.CreditNoteRefs.Count > 0;
        var originalRef = isCreditNote ? document.CreditNoteRefs[0] : null;

        var invoice = new B2BrouterInvoice
        {
            Type = SimplifiedInvoiceType,
            Number = document.Number,
            Date = FormatDate(document.IssueDate),
            Currency = document.CurrencyCode,
            SendAfterImport = sendAfterImport,
            IsCreditNote = isCreditNote,
            IsAmend = isCreditNote ? true : null,
            AmendedNumber = originalRef?.Number,
            AmendedDate = originalRef is null ? null : FormatDate(originalRef.IssueDate),
            InvoiceLines = document.Lines.Select(MapLine).ToList(),
        };

        return new B2BrouterInvoiceRequest { Invoice = invoice };
    }

    private static B2BrouterInvoiceLine MapLine(PivotLineDto line) => new()
    {
        Description = line.Description,
        Quantity = line.Quantity,
        Price = line.NetAmount,
        Tax = MapTax(line.Taxes),
    };

    // EN 16931 BG-30 : une catégorie de TVA par ligne. Le pivot peut porter plusieurs ventilations
    // (sources qui cumulent des taxes sur une ligne) ; le moteur de mapping plateforme scinde déjà en
    // une catégorie/ligne (cf. PivotLineDto.SourceRegimeCodes). On prend donc la première ventilation
    // présente. Aucune ventilation = ligne sans taxe explicite (Tax null) — B2Brouter le rejettera si
    // sa TVA est requise (fail-closed, on n'invente pas de catégorie — CLAUDE.md n°2/3).
    private static B2BrouterTax? MapTax(IReadOnlyList<PivotLineTaxDto> taxes)
    {
        if (taxes.Count == 0)
        {
            return null;
        }

        var tax = taxes[0];
        return new B2BrouterTax
        {
            Category = tax.CategoryCode?.ToString(),
            Percent = tax.Rate,
            Vatex = tax.VatexCode,
        };
    }

    private static string FormatDate(System.DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
