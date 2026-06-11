namespace Liakont.PaClients.SuperPdp;

using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Transforme le document PIVOT enrichi (EN 16931, mapping TVA déjà appliqué par la plateforme —
/// cf. <see cref="Modules.Transmission.Contracts.IPaClient"/>) vers le payload propriétaire Super PDP
/// (F14 §3.2). Le plug-in NE CALCULE RIEN et N'INVENTE AUCUNE règle fiscale (CLAUDE.md n°1/2) : il
/// recopie les montants (en <see cref="decimal"/>) et propage la catégorie UNCL5305 / le taux / le code
/// VATEX déjà portés par le pivot. La construction du payload PA-spécifique vit DANS le plug-in
/// (F14 §7 ; ajouter-un-plugin-pa §1).
/// <para>
/// PÉRIMÈTRE V1 (PAS02) : émission de facture B2C uniquement. Les AVOIRS ne sont pas émis (capacité
/// <see cref="Modules.Transmission.Contracts.PaCapabilities.SupportsCreditNotes"/> = <c>false</c>, F14 §5) :
/// un avoir est intercepté par la garde de capacité du client AVANT d'atteindre ce builder (résultat
/// typé, jamais d'exception). Le modèle d'avoir Super PDP (lien avoir→facture) est confirmé en sandbox
/// (PAS03, O7) avant d'activer la capacité — on n'invente pas un format d'avoir (CLAUDE.md n°2).
/// </para>
/// </summary>
internal static class SuperPdpPayloadBuilder
{
    // 🟠 Type de document B2C : CIBLE de conception (F14 §3.2, à confirmer OpenAPI sandbox PAS03 — O2).
    // Le modèle EN 16931 « facture simplifiée émise » est commun aux PA B2C (cf. B2Brouter F07-F08) ;
    // la valeur exacte attendue par Super PDP est figée en sandbox avant tout envoi réel (PAS03/gate).
    private const string SimplifiedInvoiceType = "IssuedSimplifiedInvoice";

    /// <summary>
    /// Construit l'enveloppe d'émission Super PDP pour un document pivot (facture B2C). La décision
    /// « facture vs avoir » n'est PAS prise ici : elle est portée par le pivot (présence de références
    /// d'origine — la classification vit dans Validation, ADR-0004 D3-3) et un avoir est déjà écarté en
    /// amont par la garde de capacité du client.
    /// </summary>
    /// <param name="document">Le document pivot enrichi à transmettre.</param>
    /// <param name="sendAfterImport">Vrai = créer ET envoyer ; faux = créé sans envoi (F14 §3.2).</param>
    public static SuperPdpInvoiceRequest Build(PivotDocumentDto document, bool sendAfterImport)
    {
        ArgumentNullException.ThrowIfNull(document);

        var invoice = new SuperPdpInvoice
        {
            Type = SimplifiedInvoiceType,
            Number = document.Number,
            Date = FormatDate(document.IssueDate),
            Currency = document.CurrencyCode,
            SendAfterImport = sendAfterImport,
            InvoiceLines = document.Lines.Select(MapLine).ToList(),
        };

        return new SuperPdpInvoiceRequest { Invoice = invoice };
    }

    private static SuperPdpInvoiceLine MapLine(PivotLineDto line) => new()
    {
        Description = line.Description,

        // NetAmount est le TOTAL HT de la ligne (EN 16931 BT-131), pas un prix unitaire. On l'émet en
        // quantité 1 pour que le total ligne = NetAmount, sans dépendre de la sémantique unit/total du
        // champ « price » (confirmée sandbox PAS03) — évite tout double comptage de la base TVA quand la
        // quantité source ≠ 1 (la quantité réelle n'est pas matérielle pour l'agrégat B2C).
        Quantity = 1m,
        Price = line.NetAmount,
        Tax = MapTax(line.Taxes),
    };

    // EN 16931 BG-30 : UNE catégorie de TVA par ligne. Le moteur de mapping plateforme (F03) scinde déjà
    // en une ventilation/ligne. Aucune ventilation = ligne sans taxe explicite (Tax null). Plusieurs
    // ventilations = contrat plateforme (BG-30) violé : on BLOQUE plutôt que de droper silencieusement une
    // taxe (sous-déclaration de TVA — CLAUDE.md n°3) ; la plateforme doit scinder la ligne avant l'envoi.
    private static SuperPdpTax? MapTax(IReadOnlyList<PivotLineTaxDto> taxes)
    {
        if (taxes.Count == 0)
        {
            return null;
        }

        if (taxes.Count > 1)
        {
            throw new InvalidOperationException(
                "Ligne avec plusieurs ventilations de TVA (EN 16931 BG-30 : une catégorie par ligne) — " +
                "le mapping plateforme doit scinder la ligne avant l'envoi à la PA.");
        }

        var tax = taxes[0];
        return new SuperPdpTax
        {
            Category = tax.CategoryCode?.ToString(),
            Percent = tax.Rate,
            Vatex = tax.VatexCode,
        };
    }

    private static string FormatDate(System.DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
