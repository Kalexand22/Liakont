namespace Liakont.Host.Profil;

/// <summary>
/// Modèle assemblé de l'écran « Paramétrage › Profil légal » (BUG-15) : le SIREN en LECTURE SEULE (clé
/// fonctionnelle immuable du tenant, INV-TENANTSETTINGS-001) et les champs éditables pré-remplis au profil
/// actuel du tenant. La page reste présentationnelle (CLAUDE.md n°19).
/// </summary>
public sealed record ProfilViewModel
{
    /// <summary>SIREN du tenant — clé fonctionnelle IMMUABLE (INV-TENANTSETTINGS-001), affichée en lecture seule.</summary>
    public required string Siren { get; init; }

    /// <summary>Champs éditables (raison sociale, adresse, contact d'alerte), pré-remplis au profil actuel.</summary>
    public required ProfilFormModel Form { get; init; }
}
