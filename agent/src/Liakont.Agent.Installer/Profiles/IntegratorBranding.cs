namespace Liakont.Agent.Installer.Profiles;

/// <summary>
/// Branding d'un profil intégrateur (F13 §5.2) : uniquement de la présentation (nom, logo, couleur),
/// JAMAIS un secret ni une donnée client (F13 §5.5, CLAUDE.md n°7). Les écrans (OPS08b) le consomment
/// pour l'habillage ; le moteur de profil n'en dépend pas.
/// </summary>
internal sealed class IntegratorBranding
{
    public IntegratorBranding(string? name, string? logo, string? primaryColor)
    {
        Name = name;
        Logo = logo;
        PrimaryColor = primaryColor;
    }

    /// <summary>Branding vide (profil sans bloc « branding »).</summary>
    public static IntegratorBranding Empty { get; } = new IntegratorBranding(null, null, null);

    /// <summary>Nom affiché de l'intégrateur (habillage du wizard).</summary>
    public string? Name { get; }

    /// <summary>Chemin relatif du logo embarqué au packaging (OPS08c), ou <c>null</c>.</summary>
    public string? Logo { get; }

    /// <summary>Couleur principale de l'habillage (ex. « #0a5 »), ou <c>null</c>.</summary>
    public string? PrimaryColor { get; }
}
