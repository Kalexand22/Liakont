namespace Liakont.Agent.Installer.Configuration;

using System;

/// <summary>
/// Résultat du test de connexion à la base source (écran source, F13 §4.1). Le test est une
/// vérification de santé en LECTURE SEULE stricte (CheckHealth / test-odbc, CLAUDE.md n°5) : aucune
/// écriture, aucun verrou. Le message est déjà rédigé en français, prêt à être affiché.
/// </summary>
internal sealed class SourceTestResult
{
    public SourceTestResult(bool success, string message)
    {
        Success = success;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>Vrai si la base source a répondu à la sonde (connexion ouverte, schéma lu).</summary>
    public bool Success { get; }

    /// <summary>Message opérateur français (succès : tables détectées ; échec : cause + correction).</summary>
    public string Message { get; }
}
