namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Adresse postale (EN 16931 BG-5 / BG-8, schéma <c>postal_address</c> de l'OpenAPI : seul
/// <c>country_code</c> est requis). Recopiée du pivot ; les champs absents sont omis du JSON.
/// </summary>
internal sealed record SuperPdpEnPostalAddress
{
    /// <summary>Première ligne d'adresse (EN 16931 BT-35 / BT-50).</summary>
    public string? AddressLine1 { get; init; }

    /// <summary>Deuxième ligne d'adresse (EN 16931 BT-36 / BT-51).</summary>
    public string? AddressLine2 { get; init; }

    /// <summary>Code postal (EN 16931 BT-38 / BT-53).</summary>
    public string? PostCode { get; init; }

    /// <summary>Ville (EN 16931 BT-37 / BT-52).</summary>
    public string? City { get; init; }

    /// <summary>Code pays ISO 3166-1 alpha-2 (EN 16931 BT-40 / BT-55) — requis par le schéma.</summary>
    public string? CountryCode { get; init; }
}
