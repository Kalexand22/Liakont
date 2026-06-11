namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Ventilation de TVA d'une ligne Super PDP. La catégorie (UNCL5305), le taux et le code VATEX sont le
/// RÉSULTAT du mapping TVA de la PLATEFORME (lot F03) porté par le pivot — jamais inventés ici
/// (CLAUDE.md n°2). Le plug-in les RECOPIE, il n'applique aucune règle fiscale.
/// </summary>
internal sealed record SuperPdpTax
{
    /// <summary>Catégorie UNCL5305 (EN 16931 BT-151), ex. <c>S</c>, <c>E</c> (F03 §2.1).</summary>
    public string? Category { get; init; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-152), ou <c>null</c> si non fourni.</summary>
    public decimal? Percent { get; init; }

    /// <summary>Code VATEX d'exonération (EN 16931 BT-121), obligatoire si catégorie E (F03 §2.2).</summary>
    public string? Vatex { get; init; }
}
