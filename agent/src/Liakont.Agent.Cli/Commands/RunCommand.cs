namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Core.Hosting;

/// <summary>
/// Commande <c>run</c> (F12 §2.1) : déclenche un run d'extraction manuel. Le run manuel et le run
/// planifié (service) partagent le MÊME verrou nommé (<see cref="InterProcessRunLock.DefaultMutexName"/>,
/// défini en AGT01) : si un run est déjà en cours, le run manuel le détecte et refuse plutôt que
/// d'extraire/pousser deux fois en parallèle. Le cycle d'extraction lui-même est injecté — son contenu
/// réel (extraction → push) est câblé par AGT02 ; cette commande porte la sérialisation et le rapport.
/// </summary>
internal sealed class RunCommand : ICliCommand
{
    private readonly Func<TextWriter, bool> _runCycle;
    private readonly string _mutexName;
    private readonly TimeSpan _acquireTimeout;

    public RunCommand(Func<TextWriter, bool> runCycle, string mutexName, TimeSpan acquireTimeout)
    {
        _runCycle = runCycle ?? throw new ArgumentNullException(nameof(runCycle));
        _mutexName = mutexName ?? throw new ArgumentNullException(nameof(mutexName));
        _acquireTimeout = acquireTimeout;
    }

    public string Name => "run";

    public string Description => "Déclenche un run d'extraction manuel (verrou partagé avec le service).";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        InterProcessRunLock? runLock = InterProcessRunLock.TryAcquire(_acquireTimeout, _mutexName);
        if (runLock is null)
        {
            output.WriteLine("Un run d'extraction est déjà en cours (lancé par le service ou une autre console). Réessayez plus tard.");
            return CliExitCode.ProblemDetected;
        }

        using (runLock)
        {
            output.WriteLine("Run d'extraction manuel démarré…");
            try
            {
                bool success = _runCycle(output);
                if (success)
                {
                    output.WriteLine("Run d'extraction terminé.");
                    return CliExitCode.Ok;
                }

                output.WriteLine(CliFormat.Fail("Le run d'extraction s'est terminé en erreur — consultez les journaux de l'agent."));
                return CliExitCode.ProblemDetected;
            }
            catch (Exception ex)
            {
                output.WriteLine(CliFormat.Fail("Échec du run d'extraction : " + ex.Message));
                return CliExitCode.ExecutionError;
            }
        }
    }
}
