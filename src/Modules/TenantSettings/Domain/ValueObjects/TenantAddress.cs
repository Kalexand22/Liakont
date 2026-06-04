namespace Liakont.Modules.TenantSettings.Domain.ValueObjects;

/// <summary>
/// Adresse de l'émetteur des flux (F12-A §2 ; cf. PivotPartyDto, F01-F02).
/// Value object immuable. Le pays est un code ISO 3166-1 alpha-2 (2 lettres majuscules).
/// </summary>
public sealed record TenantAddress(string Street, string PostalCode, string City, string Country)
{
    /// <summary>
    /// Crée une adresse en validant ses champs. Utilisé par le domaine ;
    /// la reconstitution depuis la base utilise le constructeur positionnel directement.
    /// </summary>
    public static TenantAddress Create(string street, string postalCode, string city, string country)
    {
        if (string.IsNullOrWhiteSpace(street))
        {
            throw new ArgumentException("INV-TENANTSETTINGS-002 : la rue de l'adresse est obligatoire.", nameof(street));
        }

        if (string.IsNullOrWhiteSpace(postalCode))
        {
            throw new ArgumentException("INV-TENANTSETTINGS-002 : le code postal est obligatoire.", nameof(postalCode));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("INV-TENANTSETTINGS-002 : la ville est obligatoire.", nameof(city));
        }

        if (!IsIsoCountryCode(country))
        {
            throw new ArgumentException(
                "INV-TENANTSETTINGS-002 : le pays doit être un code ISO 3166-1 alpha-2 (2 lettres majuscules).",
                nameof(country));
        }

        return new TenantAddress(street.Trim(), postalCode.Trim(), city.Trim(), country.ToUpperInvariant());
    }

    private static bool IsIsoCountryCode(string country)
    {
        return !string.IsNullOrWhiteSpace(country)
            && country.Length == 2
            && char.IsLetter(country[0])
            && char.IsLetter(country[1]);
    }
}
