namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>Détail de prix d'une ligne (EN 16931 BG-29, schéma <c>price_details</c> de l'OpenAPI).</summary>
internal sealed record SuperPdpEnLinePriceDetails
{
    /// <summary>Prix net de l'article (EN 16931 BT-146), en <see cref="decimal"/> (CLAUDE.md n°1).</summary>
    public required decimal ItemNetPrice { get; init; }
}
