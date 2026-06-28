namespace Liakont.Agent;

using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Net;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;

/// <summary>
/// Point d'entrée de l'agent. Sans argument : exécution comme service Windows (SCM). Arguments
/// reconnus : <c>install</c> / <c>uninstall</c> (auto-installation du service), <c>--console</c>
/// (exécution interactive pour le diagnostic) et l'option <c>--instance &lt;nom&gt;</c>
/// (multi-instances, OPS05 pt 5 : un service par base cliente sur un même poste — l'option est
/// inscrite dans le chemin d'image du service à l'installation). Messages opérateur en français
/// (CLAUDE.md n°12).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // TLS 1.2/1.3 forcé AU TOUT DÉBUT, avant toute connexion HTTPS sortante du service réel
        // (RDF01) : ServicePointManager est global au processus et n'est partagé ni avec test-api
        // ni avec l'updater (processus distincts).
        AgentTls.ForceStrongTls();

        if (!AgentInstance.TryFromCommandLine(args, out AgentInstance instance, out string[] remaining, out string? error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        AgentPaths.Initialize(instance);

        if (remaining.Length > 0)
        {
            switch (remaining[0].ToLowerInvariant())
            {
                case "install":
                    return RunInstaller(uninstall: false, instance);
                case "uninstall":
                    return RunInstaller(uninstall: true, instance);
                case "--console":
                case "console":
                    return RunConsole();
                default:
                    Console.Error.WriteLine(
                        $"Argument inconnu : « {remaining[0]} ». Usage : Liakont.Agent.exe [install | uninstall | --console] [--instance <nom>].");
                    return 1;
            }
        }

        using (var service = new AgentService(instance))
        {
            ServiceBase.Run(service);
        }

        return 0;
    }

    private static int RunInstaller(bool uninstall, AgentInstance instance)
    {
        try
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            var installArgs = new List<string>();
            if (uninstall)
            {
                installArgs.Add("/u");
            }

            // Le nom d'instance traverse vers AgentServiceInstaller via les paramètres d'installation
            // (Context.Parameters) — y compris à la désinstallation, pour cibler le bon service.
            installArgs.Add($"/instance={instance.Name}");
            installArgs.Add(exePath);
            ManagedInstallerClass.InstallHelper(installArgs.ToArray());

            if (uninstall)
            {
                Console.WriteLine($"Service « {instance.ServiceName} » désinstallé.");
                return PurgeLocalQueueAfterUninstall();
            }

            Console.WriteLine($"Service « {instance.ServiceName} » installé. Démarrez-le via les Services Windows ou « sc start {instance.ServiceName} ».");
            return 0;
        }
        catch (Exception ex)
        {
            string action = uninstall ? "Échec de la désinstallation du service" : "Échec de l'installation du service";
            Console.Error.WriteLine($"{action} : {ex.Message}. Relancez une console en tant qu'administrateur.");
            return 1;
        }
    }

    /// <summary>
    /// BUG-2 : purge de l'état LOCAL de l'agent (file SQLite <c>agent-queue.db</c> + annexes WAL
    /// <c>-wal</c>/<c>-shm</c>) APRÈS la désinstallation du service. Sans cette purge, le filigrane
    /// d'extraction (<c>agent_state.extraction.watermark.utc</c>) survit et une réinstallation reprend
    /// l'ancien filigrane au lieu de repartir d'un état vierge. La purge ne touche QUE le dossier de
    /// données local (<see cref="AgentPaths.DatabasePath"/>), JAMAIS la base SOURCE du client
    /// (CLAUDE.md n°5, lecture seule stricte).
    /// <para>
    /// Le service est déjà retiré : un échec de purge (fichier encore verrouillé) n'annule PAS la
    /// désinstallation mais est signalé EXPLICITEMENT (exit non nul), pour que l'opérateur supprime
    /// <c>agent-queue.db*</c> à la main plutôt que de croire l'état vierge.
    /// </para>
    /// </summary>
    private static int PurgeLocalQueueAfterUninstall()
    {
        try
        {
            int purged = LocalQueueFiles.Purge(AgentPaths.DatabasePath);
            Console.WriteLine(purged > 0
                ? $"File locale purgée ({purged} fichier(s) : agent-queue.db + annexes WAL) — la réinstallation repartira d'un état vierge."
                : "File locale déjà absente — rien à purger.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Service désinstallé, mais la file locale n'a pas pu être purgée : {ex.Message}. " +
                $"Supprimez manuellement « {AgentPaths.DatabasePath} » (+ « -wal »/« -shm ») avant toute réinstallation.");
            return 1;
        }
    }

    private static int RunConsole()
    {
        IAgentLog log = new FileAgentLog(AgentPaths.LogDirectory, new SystemClock());
        using (var host = AgentHost.Create(log))
        using (var stopped = new ManualResetEventSlim(initialState: false))
        {
            host.Start();
            Console.WriteLine($"Agent Liakont (instance « {AgentPaths.Current.Name} ») démarré en mode console. Ctrl+C pour arrêter.");

            ConsoleCancelEventHandler onCancel = (sender, e) =>
            {
                e.Cancel = true; // empêche la terminaison brutale : on arrête proprement
                stopped.Set();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                stopped.Wait();
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            Console.WriteLine("Arrêt en cours (attente de la fin du run éventuel)...");
            host.Stop(TimeSpan.FromSeconds(30));
            Console.WriteLine("Agent arrêté.");
        }

        return 0;
    }
}
