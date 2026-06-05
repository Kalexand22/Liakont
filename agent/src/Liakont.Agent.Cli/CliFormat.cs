namespace Liakont.Agent.Cli;

/// <summary>
/// Mise en forme des sorties de diagnostic (F12 §2.1) : un marqueur ✅/❌ par point de contrôle.
/// Les marqueurs sont rendus en ASCII encadré (<c>[OK]</c> / <c>[ÉCHEC]</c>) plutôt qu'en emoji :
/// la console net48 (conhost hérité) n'affiche pas toujours les caractères hors plan multilingue
/// de base — un marqueur lisible vaut mieux qu'un carré de remplacement (CLAUDE.md n°12, « lisible »).
/// </summary>
internal static class CliFormat
{
    private const string OkMark = "[OK]   ";
    private const string FailMark = "[ÉCHEC]";

    /// <summary>Ligne de point de contrôle conforme.</summary>
    public static string Ok(string text) => OkMark + " " + text;

    /// <summary>Ligne de point de contrôle en échec.</summary>
    public static string Fail(string text) => FailMark + " " + text;
}
