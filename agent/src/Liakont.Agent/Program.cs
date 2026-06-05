namespace Liakont.Agent;

using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Time;

/// <summary>
/// Point d'entrée de l'agent. Sans argument : exécution comme service Windows (SCM). Arguments
/// reconnus : <c>install</c> / <c>uninstall</c> (auto-installation du service) et <c>--console</c>
/// (exécution interactive pour le diagnostic). Messages opérateur en français (CLAUDE.md n°12).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "install":
                    return RunInstaller(uninstall: false);
                case "uninstall":
                    return RunInstaller(uninstall: true);
                case "--console":
                case "console":
                    return RunConsole();
                default:
                    Console.Error.WriteLine(
                        $"Argument inconnu : « {args[0]} ». Usage : Liakont.Agent.exe [install | uninstall | --console].");
                    return 1;
            }
        }

        using (var service = new AgentService())
        {
            ServiceBase.Run(service);
        }

        return 0;
    }

    private static int RunInstaller(bool uninstall)
    {
        try
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            var installArgs = new List<string>();
            if (uninstall)
            {
                installArgs.Add("/u");
            }

            installArgs.Add(exePath);
            ManagedInstallerClass.InstallHelper(installArgs.ToArray());

            Console.WriteLine(uninstall
                ? "Service « LiakontAgent » désinstallé."
                : "Service « LiakontAgent » installé. Démarrez-le via les Services Windows ou « sc start LiakontAgent ».");
            return 0;
        }
        catch (Exception ex)
        {
            string action = uninstall ? "Échec de la désinstallation du service" : "Échec de l'installation du service";
            Console.Error.WriteLine($"{action} : {ex.Message}. Relancez une console en tant qu'administrateur.");
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
            Console.WriteLine("Agent Liakont démarré en mode console. Ctrl+C pour arrêter.");

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
