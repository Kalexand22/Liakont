namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Une ligne du document (EN 16931 BG-25, schéma <c>invoice_line</c> de l'OpenAPI — requis :
/// <c>identifier</c>, <c>invoiced_quantity</c>, <c>invoiced_quantity_code</c>, <c>net_amount</c>,
/// <c>price_details</c>, <c>vat_information</c>, <c>item_information</c>). Montants RECOPIÉS du pivot en
/// <see cref="decimal"/> (CLAUDE.md n°1) — le plug-in ne calcule rien (F01-F02 §3.7).
/// </summary>
internal sealed record SuperPdpEnLine
{
    /// <summary>Identifiant de la ligne dans le document (EN 16931 BT-126) — rang 1..n.</summary>
    public required string Identifier { get; init; }

    /// <summary>Quantité facturée (EN 16931 BT-129).</summary>
    public required decimal InvoicedQuantity { get; init; }

    /// <summary>
    /// Unité de la quantité (EN 16931 BT-130, UN/ECE Rec 20) — requis par le schéma. Le pivot ne porte
    /// pas d'unité : <c>C62</c> (« one », l'unité neutre de la Rec 20, utilisée par la facture de test de
    /// la sandbox) — cohérent avec la quantité 1 émise (cf. <see cref="SuperPdpPayloadBuilder"/>).
    /// </summary>
    public required string InvoicedQuantityCode { get; init; }

    /// <summary>Montant net HT de la ligne (EN 16931 BT-131), recopié du pivot.</summary>
    public required decimal NetAmount { get; init; }

    /// <summary>Détail de prix (EN 16931 BG-29) — requis par le schéma.</summary>
    public required SuperPdpEnLinePriceDetails PriceDetails { get; init; }

    /// <summary>Ventilation de TVA de la ligne (EN 16931 BG-30) — requis par le schéma.</summary>
    public required SuperPdpEnLineVatInformation VatInformation { get; init; }

    /// <summary>Information d'article (EN 16931 BG-31) — requis par le schéma.</summary>
    public required SuperPdpEnLineItemInformation ItemInformation { get; init; }
}
