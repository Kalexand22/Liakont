namespace Liakont.Agent.Cli;

using System;
using System.IO;
using System.Text;
using System.Threading;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Cli.Hosting;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Net;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Core.Time;

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
        // TLS 1.2/1.3 forcé AU TOUT DÉBUT, avant toute connexion HTTPS sortante (commande `run` du
        // chemin de run réel, test-api) — RDF01. ServicePointManager est global au processus et non
        // partagé avec le service ni l'updater (processus distincts).
        AgentTls.ForceStrongTls();

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
            new RunCommand(RealRunCycle, instance.RunMutexName, RunLockAcquireTimeout),
            new ShowQueueCommand(() => LocalQueueSnapshotReader.Read(AgentPaths.DatabasePath)),
            new VersionCommand(),
        };

        return new CommandRouter(commands);
    }

    // Cycle de run RÉEL (AGT02, ADR-0031) : compose extraction → push depuis agent.json et l'exécute une
    // fois (même composition que le service via AgentRunComposition). La commande `run` porte la
    // sérialisation par verrou partagé avec le service et le rapport ; une config invalide bloque (n°3).
    private static bool RealRunCycle(TextWriter output)
    {
        var log = new FileAgentLog(AgentPaths.LogDirectory, new SystemClock());
        ComposedRunCycle composed;
        try
        {
            composed = AgentRunComposition.Build(log);
        }
        catch (AgentConfigException ex)
        {
            output.WriteLine(CliFormat.Fail("Configuration de l'agent invalide : " + ex.Message));
            return false;
        }

        using (composed)
        {
            output.WriteLine("Extraction de la source puis push vers la plateforme…");
            composed.Cycle.Run(CancellationToken.None);
        }

        output.WriteLine("Run terminé. Vérifiez l'état avec « show-queue » et la console de la plateforme.");
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
