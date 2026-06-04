namespace Liakont.Agent.Core.Logging;

using System;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Journal de l'agent (F12 §2). Messages opérateur en français (CLAUDE.md n°12). Le journal est un
/// outil de diagnostic local — il ne porte JAMAIS de secret en clair (clé API, chaîne ODBC).
/// </summary>
public interface IAgentLog
{
    void Info(string message);

    void Warn(string message);

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Info/Warn/Error sont les niveaux de sévérité standard d'un journal ; l'agent est C#-only, aucun consommateur VB/C++.")]
    void Error(string message, Exception? exception = null);
}
