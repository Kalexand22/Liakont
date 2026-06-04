namespace Liakont.Agent.Core.Tests;

using System;
using Liakont.Agent.Core.Logging;

/// <summary>Journal no-op : les tests d'hôte n'ont pas besoin d'écrire sur disque.</summary>
internal sealed class NullAgentLog : IAgentLog
{
    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
