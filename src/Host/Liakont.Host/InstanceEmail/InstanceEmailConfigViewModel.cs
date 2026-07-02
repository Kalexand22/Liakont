namespace Liakont.Host.InstanceEmail;

/// <summary>
/// Modèle de vue de la configuration email d'instance (ADR-0039) : le formulaire éditable + des booléens
/// <c>Has*</c> indiquant qu'un secret est enregistré (chiffré en base) — JAMAIS le secret lui-même (ni clair
/// ni ciphertext), patron <c>PaAccountDto</c> (INV-EMAIL-CFG-01, CLAUDE.md n°10).
/// </summary>
public sealed class InstanceEmailConfigViewModel
{
    /// <summary>Formulaire éditable (valeurs non-secrètes pré-remplies, champs secrets vides).</summary>
    public required InstanceEmailConfigForm Form { get; init; }

    /// <summary>Vrai si un mot de passe SMTP est enregistré (chiffré) — jamais la valeur.</summary>
    public bool HasSmtpPassword { get; init; }

    /// <summary>Vrai si un « client_secret » OAuth2 est enregistré (chiffré) — jamais la valeur.</summary>
    public bool HasOAuthClientSecret { get; init; }

    /// <summary>Vrai si un « refresh_token » OAuth2 est enregistré (chiffré) — jamais la valeur.</summary>
    public bool HasOAuthRefreshToken { get; init; }
}
