namespace Liakont.PaClients.SuperPdp;

using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Transforme le document PIVOT enrichi (EN 16931, mapping TVA déjà appliqué par la plateforme —
/// cf. <see cref="Modules.Transmission.Contracts.IPaClient"/>) vers le JSON <c>en16931</c> de Super PDP
/// (schéma <c>en_invoice</c>, ✅ confirmé OpenAPI + sandbox 2026-06-12 — F14 §3.2). Le plug-in N'INVENTE
/// AUCUNE règle fiscale (CLAUDE.md n°1/2) : il RECOPIE les montants (en <see cref="decimal"/>), propage
/// la catégorie UNCL5305 / le taux / le code VATEX portés par le pivot, et REGROUPE arithmétiquement les
/// ventilations de ligne (BG-30) en ventilation de document (BG-23) — sommes exactes, aucun taux ni
/// arrondi inventé. La validation EN 16931 officielle (règles <c>BR-*</c>) est appliquée par le converter
/// Super PDP : toute incohérence de la source est REJETÉE avec son message, jamais envoyée fausse
/// (CLAUDE.md n°3).
/// <para>
/// PÉRIMÈTRE V1 (PAS02) : émission de facture à destinataire IDENTIFIÉ (SIREN — gardes posées par
/// <see cref="SuperPdpClient"/> AVANT ce builder). Les AVOIRS ne sont pas émis (capacité
/// <see cref="Modules.Transmission.Contracts.PaCapabilities.SupportsCreditNotes"/> = <c>false</c>, F14 §5).
/// Les éléments que V1 ne mappe PAS (charges/remises de document BG-20/21) BLOQUENT avec un message
/// explicite plutôt que de fausser les totaux (CLAUDE.md n°3).
/// </para>
/// </summary>
internal static class SuperPdpPayloadBuilder
{
    /// <summary>
    /// Construit le document <c>en_invoice</c> pour un pivot (facture à destinataire identifié). La
    /// décision « facture vs avoir » n'est PAS prise ici : un avoir est déjà écarté en amont par la garde
    /// de capacité du client, et les gardes d'adressage (SIREN vendeur/acheteur) sont posées par le client.
    /// </summary>
    /// <param name="document">Le document pivot enrichi à transmettre.</param>
    public static SuperPdpEnInvoice Build(PivotDocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Customer is null)
        {
            // Défense en profondeur : la garde opérateur (résultat typé) vit dans SuperPdpClient.
            throw new InvalidOperationException(
                "Document sans destinataire : l'émission Super PDP exige un acheteur identifié (F14 §3.2) — " +
                "garde du client contournée.");
        }

        if (document.DocumentCharges.Count > 0)
        {
            // BG-20/21 non mappés en V1 : les omettre fausserait les totaux (BR-CO-13) — bloquer plutôt
            // qu'envoyer faux (CLAUDE.md n°3).
            throw new InvalidOperationException(
                "Charges/remises de niveau document (EN 16931 BG-20/BG-21) non prises en charge par " +
                "l'émission Super PDP V1 — document à transmettre par un autre canal ou lot à faire évoluer.");
        }

        return new SuperPdpEnInvoice
        {
            Number = document.Number,
            IssueDate = FormatDate(document.IssueDate),

            // EN 16931 BT-9 (EXT01) : RECOPIÉE du pivot quand elle est portée, sinon OMISE (null) — une
            // échéance n'est JAMAIS fabriquée (CLAUDE.md n°2). Sa présence lève BR-CO-25 pour un montant
            // dû positif ; son absence conserve le rejet du converter, message intact (F14 §3.2/O11).
            PaymentDueDate = document.PaymentDueDate.HasValue ? FormatDate(document.PaymentDueDate.Value) : null,
            TypeCode = SuperPdpDefaults.CommercialInvoiceTypeCode,
            CurrencyCode = document.CurrencyCode,
            ProcessControl = new SuperPdpEnProcessControl
            {
                SpecificationIdentifier = SuperPdpDefaults.SpecificationIdentifier,
            },
            Seller = MapParty(document.Supplier!),
            Buyer = MapParty(document.Customer),
            Totals = MapTotals(document),
            VatBreakDown = BuildVatBreakDown(document.Lines),
            Lines = document.Lines.Select(MapLine).ToList(),
        };
    }

    // Recopie une partie pivot (BG-4 vendeur / BG-7 acheteur). Le SIREN porte l'identification légale
    // (BT-30, scheme 0002) ET l'adressage d'annuaire (BT-34/BT-49, scheme 0002 — ✅ validé sandbox,
    // F14 §3.2). Aucun identifiant inventé : un champ absent du pivot est omis, la validation EN 16931
    // du converter et les contrôles d'envoi de la PA tranchent (messages conservés intacts).
    private static SuperPdpEnParty MapParty(PivotPartyDto party)
    {
        var siren = string.IsNullOrWhiteSpace(party.Siren) ? null : party.Siren;
        var identifier = siren is null
            ? null
            : new SuperPdpEnIdentifier { Value = siren, Scheme = SuperPdpDefaults.SirenScheme };
        return new SuperPdpEnParty
        {
            Name = party.Name,
            LegalRegistrationIdentifier = identifier,
            VatIdentifier = string.IsNullOrWhiteSpace(party.VatNumber) ? null : party.VatNumber,
            ElectronicAddress = identifier,
            PostalAddress = MapAddress(party.Address),
        };
    }

    private static SuperPdpEnPostalAddress? MapAddress(PivotAddressDto? address) =>
        address is null
            ? null
            : new SuperPdpEnPostalAddress
            {
                AddressLine1 = address.Line1,
                AddressLine2 = address.Line2,
                PostCode = address.PostalCode,
                City = address.City,
                CountryCode = address.CountryCode,
            };

    // Totaux BG-22 RECOPIÉS du pivot (calculés par la source — F01-F02 §3.7). Sans charges/remises de
    // document (garde ci-dessus), la somme des lignes (BT-106) est ÉGALE au total HT (BT-109) par
    // construction de la source. L'acompte du pivot est émis en BT-113 et le montant dû (BT-115)
    // applique l'identité normative BR-CO-16 (BT-115 = BT-112 − BT-113) — une identité de la norme
    // EN 16931, pas une règle inventée ; soustraction exacte en decimal (CLAUDE.md n°1). NB : un montant
    // dû POSITIF exige BT-9/BT-20 (BR-CO-25). Depuis EXT01, le pivot peut porter BT-9 (PaymentDueDate,
    // émise ci-dessus dans Build) : présente ⇒ BR-CO-25 satisfaite ; absente ⇒ le converter rejette avec
    // son message, conservé intact (F14 §3.2/O11 — aucune échéance fabriquée).
    private static SuperPdpEnTotals MapTotals(PivotDocumentDto document) => new()
    {
        SumInvoiceLinesAmount = document.Totals.TotalNet,
        TotalWithoutVat = document.Totals.TotalNet,
        TotalVatAmount = new SuperPdpEnAmount
        {
            Value = document.Totals.TotalTax,
            CurrencyCode = document.CurrencyCode,
        },
        TotalWithVat = document.Totals.TotalGross,
        PaidAmount = document.PrepaidAmount,
        AmountDueForPayment = document.Totals.TotalGross - (document.PrepaidAmount ?? 0m),
    };

    // BG-23 : REGROUPEMENT des ventilations de ligne (BG-30) par (catégorie, taux, VATEX) avec sommes
    // exactes en decimal — base = montants nets des lignes du groupe, TVA = somme des TVA de ligne.
    // Aucune catégorie, aucun taux, aucun arrondi inventés : tout vient du pivot (mapping plateforme F03).
    private static List<SuperPdpEnVatBreakDown> BuildVatBreakDown(IReadOnlyList<PivotLineDto> lines) =>
        lines
            .Select(line => (Line: line, Tax: SingleTax(line)))
            .GroupBy(x => (x.Tax.CategoryCode!.Value, x.Tax.Rate, x.Tax.VatexCode))
            .Select(group => new SuperPdpEnVatBreakDown
            {
                VatCategoryTaxableAmount = group.Sum(x => x.Line.NetAmount),
                VatCategoryTaxAmount = group.Sum(x => x.Tax.TaxAmount),
                VatCategoryCode = group.Key.Value.ToString(),
                VatCategoryRate = group.Key.Rate,
                VatExemptionReasonCode = group.Key.VatexCode,
            })
            .ToList();

    private static SuperPdpEnLine MapLine(PivotLineDto line, int index)
    {
        var tax = SingleTax(line);

        // NetAmount est le TOTAL HT de la ligne (EN 16931 BT-131), pas un prix unitaire. On l'émet en
        // quantité 1 (unité neutre C62) pour que quantité × prix = total ligne, sans dépendre de la
        // sémantique quantité/prix de la source — évite tout double comptage de la base TVA quand la
        // quantité source ≠ 1 (les lignes pivot des adaptateurs V1 sont des agrégats : adjudication/frais).
        return new SuperPdpEnLine
        {
            Identifier = (index + 1).ToString(CultureInfo.InvariantCulture),
            InvoicedQuantity = 1m,

            // BT-130 : la ligne SuperPDP est un agrégat SYNTHÉTIQUE émis en quantité 1 (cf. ci-dessus) ;
            // sa seule unité cohérente est l'unité neutre « one » (C62). On NE projette PAS l'UnitCode du
            // pivot (RD407) ici : « 1 KGM » au prix du total ligne serait fiscalement incohérent (CLAUDE.md
            // n°3). L'émission fidèle de BT-130 côté SuperPDP suppose d'émettre l'unité AVEC la quantité
            // réelle — donc de revoir la normalisation quantité=1 — différé B2B (phase 2). FacturX, lui,
            // émet la quantité réelle (BT-129) et projette donc l'UnitCode fidèlement.
            InvoicedQuantityCode = SuperPdpDefaults.DefaultQuantityUnitCode,
            NetAmount = line.NetAmount,
            PriceDetails = new SuperPdpEnLinePriceDetails { ItemNetPrice = line.NetAmount },
            VatInformation = new SuperPdpEnLineVatInformation
            {
                InvoicedItemVatCategoryCode = tax.CategoryCode!.Value.ToString(),
                InvoicedItemVatRate = tax.Rate,
            },
            ItemInformation = new SuperPdpEnLineItemInformation { Name = line.Description },
        };
    }

    // EN 16931 BG-30 : UNE catégorie de TVA par ligne, AVEC catégorie posée. Le moteur de mapping
    // plateforme (F03) scinde déjà en une ventilation/ligne et pose la catégorie. Aucune ventilation ou
    // catégorie absente = contrat plateforme violé : le schéma en_invoice EXIGE vat_information par ligne
    // (F14 §3.2) — on BLOQUE avec un message explicite plutôt que d'inventer une catégorie ou de droper
    // une taxe (sous-déclaration de TVA — CLAUDE.md n°2/3). Plusieurs ventilations = la plateforme doit
    // scinder la ligne avant l'envoi.
    private static PivotLineTaxDto SingleTax(PivotLineDto line)
    {
        if (line.Taxes.Count != 1)
        {
            throw new InvalidOperationException(
                line.Taxes.Count == 0
                    ? "Ligne sans ventilation de TVA : le schéma en_invoice de Super PDP exige la catégorie " +
                      "de TVA par ligne (EN 16931 BG-30) — le mapping plateforme doit ventiler la ligne avant l'envoi."
                    : "Ligne avec plusieurs ventilations de TVA (EN 16931 BG-30 : une catégorie par ligne) — " +
                      "le mapping plateforme doit scinder la ligne avant l'envoi à la PA.");
        }

        var tax = line.Taxes[0];
        if (tax.CategoryCode is null)
        {
            throw new InvalidOperationException(
                "Ventilation de TVA sans catégorie UNCL5305 : le mapping plateforme (F03) doit poser la " +
                "catégorie avant l'envoi à la PA — aucune catégorie n'est inventée ici (CLAUDE.md n°2).");
        }

        return tax;
    }

    private static string FormatDate(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
