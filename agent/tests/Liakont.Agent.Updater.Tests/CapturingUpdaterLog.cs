namespace Liakont.Agent.Updater.Tests;

using System.Collections.Generic;

/// <summary>Journal d'updater capturant les messages, pour vérifier les signalements.</summary>
internal sealed class CapturingUpdaterLog : IUpdaterLog
{
    public List<string> Messages { get; } = new List<string>();

    public void Write(string message) => Messages.Add(message);
}
