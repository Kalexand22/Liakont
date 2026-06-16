namespace Liakont.Modules.SupportTrace.Infrastructure;

/// <summary>
/// Options de la trace de support du Factur-X (section de configuration <c>SupportTrace</c>). La racine est un
/// PARAMÉTRAGE d'INSTANCE (jamais en dur — CLAUDE.md n°7) ; une instance de production configure un volume
/// dédié, distinct du coffre d'archive (la trace de support est transitoire et purgeable). La rétention est un
/// PARAMÉTRAGE (proposition 90 jours, F16 §10), jamais un seuil fiscal en dur (CLAUDE.md n°2).
/// </summary>
public sealed class SupportTraceOptions
{
    /// <summary>Valeur de rétention par défaut retenue par F16 §7/§10 (proposition, configurable).</summary>
    public const int DefaultRetentionDays = 90;

    /// <summary>Racine du store de trace de support sur le système de fichiers de l'instance.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Fenêtre de rétention en jours ; au-delà, la trace de support est purgeable (défaut 90, F16 §10).</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;
}
