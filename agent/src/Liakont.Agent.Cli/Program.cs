namespace Liakont.Agent.Cli;

using System;
using System.IO;
using System.Text;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Security;

/// <summary>
/// CLI de diagnostic et de mise en service de l'agent (F12 §2.1, AGT05) : check-config, test-odbc,
/// test-api, encrypt, run, show-queue, version. Outil utilisé par l'intégrateur/éditeur ; messages
/// 100 % français (CLAUDE.md n°12) ; codes de retour 0 = OK, 1 = problème détecté, 2 = erreur.
/// <para>
/// Option globale <c>--instance &lt;nom&gt;</c> (multi-instances, OPS05 pt 5) : cible la
/// configuration, la file locale et le verrou de run de CETTE instance (défaut : Default).
/// </para>
/// <para>
/// Composition root : il câble les commandes à leurs dépendances réelles (DPAPI, sondes ODBC/HTTP,
/// file locale, verrou de run partagé). Les commandes elles-mêmes restent testables avec des doublures.
/// </para>
/// </summary>
internal static class Program
{
    // Détection de contention du verrou de run : court délai pour distinguer « déjà en cours » d'une
    // simple course de démarrage, sans bloquer l'intégrateur.
    private static readonly TimeSpan RunLockAcquireTimeout = TimeSpan.FromSeconds(2);

    private static int Main(string[] args)
    {
        TryEnableUtf8Console();

        if (!AgentInstance.TryFromCommandLine(args, out AgentInstance instance, out string[] remaining, out string? instanceError))
        {
            Console.Error.WriteLine(instanceError);
            return CliExitCode.ExecutionError;
        }

        AgentPaths.Initialize(instance);

        try
        {
            CommandRouter router = BuildRouter(instance);
            return router.Execute(remaining, Console.Out);
        }
        catch (Exception ex)
        {
            // Filet de sécurité : aucune commande ne devrait remonter d'exception, mais le CLI doit
            // toujours rendre un code de retour exploitable plutôt que de planter avec une stack trace.
            Console.Error.WriteLine("Erreur inattendue du CLI : " + ex.Message);
            return CliExitCode.ExecutionError;
        }
    }

    private static CommandRouter BuildRouter(AgentInstance instance)
    {
        ISecretProtector protector = new DpapiSecretProtector();
        string configPath = AgentPaths.ConfigPath;
        string[] knownAdapters = EmbeddedSourceAdapters.Names();

        var commands = new ICliCommand[]
        {
            new CheckConfigCommand(configPath, protector, knownAdapters),
            new TestOdbcCommand(configPath, protector, OdbcProbe.Probe),
            new TestApiCommand(configPath, protector, HttpPlatformProbe.Probe),
            new EncryptCommand(protector, Console.In),
            new RunCommand(NeutralRunCycle, instance.RunMutexName, RunLockAcquireTimeout),
            new ShowQueueCommand(() => LocalQueueSnapshotReader.Read(AgentPaths.DatabasePath)),
            new VersionCommand(),
        };

        return new CommandRouter(commands);
    }

    // Cycle de run NEUTRE tant qu'AGT02 n'a pas câblé l'extraction/le push (même seam que
    // AgentHost.Create côté service). La commande `run` porte ici la sérialisation par verrou partagé
    // et le rapport ; le contenu réel du cycle sera injecté avec AGT02.
    private static bool NeutralRunCycle(TextWriter output)
    {
        output.WriteLine("Extraction non encore câblée dans cette version (fournie par AGT02) — aucun document traité.");
        return true;
    }

    private static void TryEnableUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // La console peut refuser le changement d'encodage (sortie redirigée vers un handle qui ne
            // le supporte pas). Sans gravité : les messages restent lisibles dans l'encodage par défaut.
        }
    }
}
