namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>Information d'article d'une ligne (EN 16931 BG-31, schéma <c>item_information</c> de l'OpenAPI).</summary>
internal sealed record SuperPdpEnLineItemInformation
{
    /// <summary>Nom de l'article (EN 16931 BT-153) — le libellé de la ligne pivot.</summary>
    public required string Name { get; init; }
}
