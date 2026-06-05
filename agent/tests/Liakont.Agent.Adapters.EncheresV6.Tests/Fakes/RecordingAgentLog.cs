namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Journal d'agent enregistreur pour les tests : capture les messages par niveau, ne lève jamais
/// (contrat <see cref="IAgentLog"/>). Sert à prouver le Warning « PDF introuvable / dossier absent »
/// (acceptance ADP05) sans toucher au système de fichiers de journalisation réel.
/// </summary>
public sealed class RecordingAgentLog : IAgentLog
{
    /// <summary>Messages de niveau Info capturés, dans l'ordre.</summary>
    public List<string> Infos { get; } = new List<string>();

    /// <summary>Messages de niveau Warn capturés, dans l'ordre.</summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>Messages de niveau Error capturés, dans l'ordre.</summary>
    public List<string> Errors { get; } = new List<string>();

    /// <inheritdoc />
    public void Info(string message) => Infos.Add(message);

    /// <inheritdoc />
    public void Warn(string message) => Warnings.Add(message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}
