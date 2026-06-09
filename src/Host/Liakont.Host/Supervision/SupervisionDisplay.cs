namespace Liakont.Host.Supervision;

/// <summary>
/// Vocabulaire opérateur FR de la supervision (CLAUDE.md n°12) : la gravité est restituée en TEXTE par les
/// lectures (fidélité à la base — <c>Critical</c> / <c>Warning</c>) ; l'UI la traduit en libellé opérateur
/// (🔴 Critique / 🟠 Avertissement). Mapping centralisé, partagé par la vue d'ensemble et le détail.
/// </summary>
internal static class SupervisionDisplay
{
    /// <summary>Gravité critique (texte de base).</summary>
    public const string Critical = "Critical";

    /// <summary>Gravité avertissement (texte de base).</summary>
    public const string Warning = "Warning";

    /// <summary>Libellé FR d'une gravité (texte de base <c>Critical</c>/<c>Warning</c>), ou tiret si nulle/inconnue.</summary>
    public static string SeverityLabel(string? severity) => severity switch
    {
        Critical => "🔴 Critique",
        Warning => "🟠 Avertissement",
        _ => "—",
    };
}
