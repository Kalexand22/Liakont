namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Logging;

/// <summary>Journal capturant les messages, pour vérifier les signalements opérateur (AGT02).</summary>
internal sealed class CapturingAgentLog : IAgentLog
{
    public List<string> Infos { get; } = new List<string>();

    public List<string> Warnings { get; } = new List<string>();

    public List<string> Errors { get; } = new List<string>();

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}
