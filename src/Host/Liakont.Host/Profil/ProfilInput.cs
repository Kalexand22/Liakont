namespace Liakont.Host.Profil;

/// <summary>
/// Valeurs brutes du formulaire de profil légal transmises au service à l'enregistrement (BUG-15). Le SIREN
/// n'y figure PAS : immuable (INV-TENANTSETTINGS-001), il est repris du profil persisté par le service. Le
/// service normalise la chaîne d'e-mail vide en <c>null</c> et délègue la validation (adresse, code pays,
/// e-mail) au handler / domaine (CLAUDE.md n°2/3).
/// </summary>
public sealed record ProfilInput
{
    /// <summary>Raison sociale du tenant.</summary>
    public required string RaisonSociale { get; init; }

    /// <summary>Voie de l'adresse du siège.</summary>
    public required string Street { get; init; }

    /// <summary>Code postal du siège.</summary>
    public required string PostalCode { get; init; }

    /// <summary>Ville du siège.</summary>
    public required string City { get; init; }

    /// <summary>Code pays ISO 3166-1 alpha-2 du siège.</summary>
    public required string Country { get; init; }

    /// <summary>Adresse e-mail destinataire des alertes du tenant (facultatif).</summary>
    public string? ContactEmailAlerte { get; init; }
}
