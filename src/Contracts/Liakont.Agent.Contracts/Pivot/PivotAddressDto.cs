namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Adresse postale d'un tiers (EN 16931 BG-5 / BG-8). DTO pur : aucune logique, aucune validation.
/// Tout champ absent est <c>null</c> — jamais une valeur devinée (F01-F02 §3.7 règle 3).
/// </summary>
public sealed class PivotAddressDto
{
    /// <summary>Crée une adresse pivot. Tous les champs sont optionnels (absent = <c>null</c>).</summary>
    /// <param name="line1">Voie / ligne 1.</param>
    /// <param name="line2">Complément d'adresse (ligne 2).</param>
    /// <param name="postalCode">Code postal.</param>
    /// <param name="city">Ville.</param>
    /// <param name="countryCode">Pays au format ISO 3166-1 alpha-2 (EN 16931 BT-40 / BT-55).</param>
    public PivotAddressDto(
        string? line1 = null,
        string? line2 = null,
        string? postalCode = null,
        string? city = null,
        string? countryCode = null)
    {
        Line1 = line1;
        Line2 = line2;
        PostalCode = postalCode;
        City = city;
        CountryCode = countryCode;
    }

    /// <summary>Voie / ligne 1.</summary>
    public string? Line1 { get; }

    /// <summary>Complément d'adresse (ligne 2).</summary>
    public string? Line2 { get; }

    /// <summary>Code postal.</summary>
    public string? PostalCode { get; }

    /// <summary>Ville.</summary>
    public string? City { get; }

    /// <summary>Pays au format ISO 3166-1 alpha-2 (EN 16931 BT-40 / BT-55).</summary>
    public string? CountryCode { get; }
}
