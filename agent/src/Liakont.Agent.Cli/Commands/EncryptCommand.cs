namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Core.Security;

/// <summary>
/// Commande <c>encrypt</c> (F12 §2.1) : chiffre une valeur (clé API, chaîne ODBC) en DPAPI portée
/// machine pour collage dans <c>agent.json</c>. La valeur est lue sur l'entrée standard si elle n'est
/// pas passée en argument — éviter qu'un secret n'apparaisse dans l'historique du shell ou la liste
/// des processus.
/// </summary>
internal sealed class EncryptCommand : ICliCommand
{
    private readonly ISecretProtector _protector;
    private readonly TextReader _input;

    public EncryptCommand(ISecretProtector protector, TextReader input)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public string Name => "encrypt";

    public string Description => "Chiffre une valeur (DPAPI) à coller dans agent.json.";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        string? value = args.Count > 0 ? args[0] : _input.ReadLine();

        if (string.IsNullOrEmpty(value))
        {
            output.WriteLine("Aucune valeur à chiffrer. Usage : encrypt <valeur>  (ou fournissez-la sur l'entrée standard).");
            return CliExitCode.ExecutionError;
        }

        string protectedValue = _protector.Protect(value);
        output.WriteLine("Valeur chiffrée (à coller dans agent.json) :");
        output.WriteLine(protectedValue);
        return CliExitCode.Ok;
    }
}
