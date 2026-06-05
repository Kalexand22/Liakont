namespace Liakont.Agent.Cli.Tests;

using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Cli;

/// <summary>Commande factice : enregistre les arguments reçus et renvoie un code de retour configuré.</summary>
internal sealed class FakeCommand : ICliCommand
{
    private readonly int _exitCode;

    public FakeCommand(string name, int exitCode)
    {
        Name = name;
        _exitCode = exitCode;
    }

    public string Name { get; }

    public string Description => "Commande factice de test.";

    public bool WasInvoked { get; private set; }

    public IReadOnlyList<string>? ReceivedArgs { get; private set; }

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        WasInvoked = true;
        ReceivedArgs = args;
        return _exitCode;
    }
}
