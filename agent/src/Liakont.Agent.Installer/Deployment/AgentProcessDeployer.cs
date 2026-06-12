namespace Liakont.Agent.Installer.Deployment;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

        if (!TryRunServiceInstaller(instance, uninstall: false, out string serviceMessage))
        {
            report.Add(serviceMessage);
            return new DeploymentOutcome(false, report);
        }

        report.Add(serviceMessage);

        bool configOk = RunCheckConfig(configPath, report);
        if (!configOk)
        {
            report.Add(
                $"[!]     Le service « {instance.ServiceName} » est INSTALLÉ mais la configuration ci-dessus " +
                "présente un problème : corrigez agent.json puis relancez le test, ou désinstallez cette " +
                $"instance (--uninstall {instance.Name}).");
        }

        return new DeploymentOutcome(configOk, report);
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
