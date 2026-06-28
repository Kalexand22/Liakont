namespace Liakont.Host.Profil;

/// <summary>
/// Valeurs éditables du profil légal du tenant (BUG-15) : raison sociale, adresse et contact d'alerte. Le
/// SIREN n'y figure PAS — clé fonctionnelle immuable (INV-TENANTSETTINGS-001), affiché en lecture seule et
/// repassé inchangé à l'enregistrement par le service (jamais saisi côté client). Aucune logique de validation
/// ici : la cohérence (adresse, code pays, e-mail) reste du ressort du handler / domaine TenantSettings.
/// </summary>
public sealed class ProfilFormModel
{
    /// <summary>Raison sociale du tenant.</summary>
    public string RaisonSociale { get; set; } = string.Empty;

    /// <summary>Voie de l'adresse du siège.</summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>Code postal du siège.</summary>
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>Ville du siège.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Code pays ISO 3166-1 alpha-2 (2 lettres majuscules) du siège.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Adresse e-mail destinataire des alertes du tenant (facultatif).</summary>
    public string? ContactEmailAlerte { get; set; }
}
