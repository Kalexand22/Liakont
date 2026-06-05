namespace Liakont.Agent.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Aiguille un appel CLI vers la commande nommée (F12 §2.1). Sans argument ou avec <c>help</c>,
/// affiche l'aide (succès). Une commande inconnue est une erreur d'exécution (<see cref="CliExitCode.ExecutionError"/>),
/// pas un « problème détecté » : l'intégrateur s'est trompé d'invocation, rien n'a été diagnostiqué.
/// </summary>
internal sealed class CommandRouter
{
    private static readonly string[] HelpAliases = { "help", "--help", "-h", "/?" };

    private readonly IReadOnlyList<ICliCommand> _commands;

    public CommandRouter(IReadOnlyList<ICliCommand> commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (args is null || args.Count == 0 || HelpAliases.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            WriteUsage(output);
            return CliExitCode.Ok;
        }

        string name = args[0];
        ICliCommand? command = _commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            output.WriteLine($"Commande inconnue : « {name} ».");
            WriteUsage(output);
            return CliExitCode.ExecutionError;
        }

        // Le reste des arguments (sans le nom de la commande) est passé à la commande.
        var commandArgs = args.Skip(1).ToArray();
        return command.Execute(commandArgs, output);
    }

    private void WriteUsage(TextWriter output)
    {
        output.WriteLine("Liakont Agent — CLI de diagnostic et de mise en service.");
        output.WriteLine("Usage : liakont-agent-cli <commande> [arguments]");
        output.WriteLine();
        output.WriteLine("Commandes :");
        foreach (ICliCommand command in _commands.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            output.WriteLine($"  {command.Name,-13} {command.Description}");
        }

        output.WriteLine();
        output.WriteLine("Codes de retour : 0 = OK, 1 = problème détecté, 2 = erreur d'exécution.");
    }
}
