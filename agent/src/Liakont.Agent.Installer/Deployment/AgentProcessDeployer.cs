namespace Liakont.Agent.Installer.Deployment;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Security;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Implémentation de production de <see cref="IAgentDeployer"/> : effectue les E/S réelles du déploiement
/// d'une instance (F13 §4, étape finale) — écriture de <c>agent.json</c> (secrets déjà chiffrés DPAPI),
/// self-install du service Windows via <c>Liakont.Agent.exe install --instance &lt;nom&gt;</c> (AGT01),
/// puis <c>check-config</c> final RÉUTILISÉ en in-process (<see cref="CheckConfigCommand"/>, AGT05 — aucune
/// logique dupliquée). Chaque instance est ciblée par son nom (chemins/service dérivés par
/// <see cref="AgentInstance"/>) : installer une nouvelle instance ne touche jamais les autres ; la
/// désinstallation cible une instance précise. La pose des binaires relève du packaging (OPS08c) : ce
/// déployeur suppose l'exécutable du service présent à côté de l'installeur (cas du paquet auto-suffisant).
/// </summary>
internal sealed class AgentProcessDeployer : IAgentDeployer
{
    private const string ServiceExecutableName = "Liakont.Agent.exe";
    private const string ConfigFileName = "agent.json";
    private static readonly string[] LineSeparators = { "\r\n", "\n" };

    /// <summary>Délai d'attente du passage « En cours d'exécution » après le démarrage du service (RB7).</summary>
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(30);

    private readonly ISecretProtector _protector;
    private readonly IReadOnlyCollection<string> _knownAdapters;

    public AgentProcessDeployer(ISecretProtector protector, IReadOnlyCollection<string> knownAdapters)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _knownAdapters = knownAdapters ?? throw new ArgumentNullException(nameof(knownAdapters));
    }

    /// <inheritdoc />
    public DeploymentOutcome Install(InstallationPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!AgentInstance.TryParse(plan.InstanceName, out AgentInstance instance, out string? error))
        {
            return DeploymentOutcome.Failure(error!);
        }

        var report = new List<string>();

        string configPath = Path.Combine(instance.DataDirectory, ConfigFileName);
        try
        {
            Directory.CreateDirectory(instance.DataDirectory);
            File.WriteAllText(configPath, plan.AgentJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            report.Add($"[OK]    agent.json écrit : {configPath} (secrets chiffrés DPAPI).");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return DeploymentOutcome.Failure(
                $"Écriture de « {ConfigFileName} » impossible pour l'instance « {instance.Name} » : {ex.Message} " +
                "Relancez l'installeur en tant qu'administrateur.");
        }

        // Valider la configuration AVANT d'installer le service : ne JAMAIS enregistrer un service
        // Windows (potentiellement auto-démarré) avec une configuration invalide. Un agent.json qui
        // passe le round-trip de Build mais échoue check-config (cas réel en mode silencieux : adaptateur
        // hors de la liste embarquée) doit BLOQUER l'installation, sans rien laisser derrière
        // (round 2 #1 ; CLAUDE.md n°3 : bloquer plutôt qu'installer un agent cassé).
        if (!RunCheckConfig(configPath, report))
        {
            report.Add(
                $"[!]     Configuration invalide : le service « {instance.ServiceName} » n'a PAS été installé. " +
                "Corrigez agent.json (détails ci-dessus) puis relancez l'installation.");
            return new DeploymentOutcome(false, report);
        }

        if (!TryRunServiceInstaller(instance, uninstall: false, out string serviceMessage))
        {
            report.Add(serviceMessage);
            return new DeploymentOutcome(false, report);
        }

        report.Add(serviceMessage);

        // RB7 : démarrer le service IMMÉDIATEMENT (un install réussi = un agent qui tourne). Le service est
        // enregistré en démarrage AUTOMATIQUE (il repartirait au prochain boot), mais l'opérateur ne doit pas
        // avoir à le lancer à la main après l'assistant. Un échec de démarrage NE défait PAS l'install (le
        // service reste enregistré) : on le signale en avertissement avec l'action corrective (CLAUDE.md n°12).
        report.Add(TryStartService(instance));
        return new DeploymentOutcome(true, report);
    }

    /// <inheritdoc />
    public DeploymentOutcome Uninstall(string instanceName)
    {
        if (!AgentInstance.TryParse(instanceName, out AgentInstance instance, out string? error))
        {
            return DeploymentOutcome.Failure(error!);
        }

        var report = new List<string>();
        if (!TryRunServiceInstaller(instance, uninstall: true, out string serviceMessage))
        {
            report.Add(serviceMessage);
            return new DeploymentOutcome(false, report);
        }

        report.Add(serviceMessage);
        report.Add($"[OK]    Instance « {instance.Name} » désinstallée. Les autres instances ne sont pas affectées.");
        return new DeploymentOutcome(true, report);
    }

    private static bool TryRunServiceInstaller(AgentInstance instance, bool uninstall, out string message)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string exePath = Path.Combine(baseDir, ServiceExecutableName);
        if (!File.Exists(exePath))
        {
            message =
                $"[ÉCHEC] Exécutable du service introuvable ({exePath}). " +
                "Lancez l'installeur depuis le dossier contenant les binaires de l'agent.";
            return false;
        }

        string verb = uninstall ? "uninstall" : "install";
        var startInfo = new ProcessStartInfo(exePath)
        {
            Arguments = $"{verb} {AgentInstance.CommandLineOption} {instance.Name}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using (Process? process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    message = $"[ÉCHEC] Lancement de « {ServiceExecutableName} {verb} » impossible.";
                    return false;
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                string standardOutput = outputTask.GetAwaiter().GetResult();
                string standardError = errorTask.GetAwaiter().GetResult();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    message = $"[OK]    Service « {instance.ServiceName} » {(uninstall ? "désinstallé" : "installé")}.";
                    return true;
                }

                string detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                message =
                    $"[ÉCHEC] {(uninstall ? "Désinstallation" : "Installation")} du service « {instance.ServiceName} » : " +
                    detail.Trim();
                return false;
            }
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            message =
                $"[ÉCHEC] {(uninstall ? "Désinstallation" : "Installation")} du service « {instance.ServiceName} » : " +
                $"{ex.Message} Relancez l'installeur en tant qu'administrateur.";
            return false;
        }
    }

    /// <summary>
    /// Démarre le service de l'instance et attend qu'il passe « En cours d'exécution » (RB7). Renvoie une
    /// ligne de rapport (français, CLAUDE.md n°12) : succès, déjà démarré, ou avertissement avec l'action
    /// corrective si le démarrage automatique échoue (le service reste installé, l'install n'est pas défaite).
    /// <para>
    /// COUVERTURE : comme tout le déployeur de PRODUCTION (E/S réelles : <see cref="TryRunServiceInstaller"/>,
    /// écriture de agent.json), ce chemin pilote le SCM Windows réel (<see cref="ServiceController"/>, non
    /// mockable sans abstraction). Il n'est donc PAS couvert par un test unitaire (les tests passent par le
    /// fake <c>RecordingDeployer</c>) : le démarrage EFFECTIF est vérifié en RECETTE manuelle (RB7). On
    /// n'extrait pas d'<c>IServiceController</c> tant que ce besoin de testabilité ne se concrétise pas
    /// (pas de sur-architecture au stade build).
    /// </para>
    /// </summary>
    private static string TryStartService(AgentInstance instance)
    {
        try
        {
            using (var controller = new ServiceController(instance.ServiceName))
            {
                if (controller.Status == ServiceControllerStatus.Running)
                {
                    return $"[OK]    Service « {instance.ServiceName} » déjà en cours d'exécution.";
                }

                if (controller.Status != ServiceControllerStatus.StartPending)
                {
                    controller.Start();
                }

                controller.WaitForStatus(ServiceControllerStatus.Running, ServiceStartTimeout);
                return $"[OK]    Service « {instance.ServiceName} » démarré (en cours d'exécution).";
            }
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return
                $"[!]     Service « {instance.ServiceName} » installé mais pas encore passé « En cours d'exécution » " +
                $"dans le délai imparti. Consultez les journaux de l'agent ; au besoin, démarrez-le via les " +
                $"Services Windows ou « sc start {instance.ServiceName} ».";
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
        {
            return
                $"[!]     Service « {instance.ServiceName} » installé mais le démarrage automatique a échoué : " +
                $"{ex.Message} Démarrez-le via les Services Windows ou « sc start {instance.ServiceName} ».";
        }
    }

    private bool RunCheckConfig(string configPath, List<string> report)
    {
        using (var writer = new StringWriter(CultureInfo.CurrentCulture))
        {
            var command = new CheckConfigCommand(configPath, _protector, _knownAdapters);
            int code = command.Execute(new[] { configPath }, writer);
            foreach (string line in writer.ToString().Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                report.Add(line);
            }

            return code == 0;
        }
    }
}
