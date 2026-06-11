namespace Liakont.Modules.Archive.Application;

/// <summary>
/// Tranche BRANDING d'instance que porte la notice de l'export de réversibilité (BRD01, « marque grise »,
/// blueprint.md §3.3, F12 §6.1). Instance-level : la notice porte la marque de l'ÉDITEUR (le nom commercial)
/// et, si activée, la mention technique discrète « propulsé par Liakont ». Construite par
/// <c>ArchiveModuleRegistration</c> depuis la section appsettings <c>Branding</c> (la même que la coquille
/// et les emails). Valeurs par défaut = marque produit « Liakont » (aucune donnée client — CLAUDE.md n°7).
/// </summary>
public sealed record ReversibilityBranding(string CommercialName, bool PoweredByLiakont)
{
    /// <summary>Marque produit par défaut, utilisée quand l'instance ne configure pas la section.</summary>
    public const string DefaultCommercialName = "Liakont";

    /// <summary>Branding par défaut (« Liakont », mention propulsé affichée) — repli hors composition root.</summary>
    public static ReversibilityBranding Default { get; } = new(DefaultCommercialName, PoweredByLiakont: true);
}
