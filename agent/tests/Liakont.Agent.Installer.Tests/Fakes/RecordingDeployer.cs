namespace Liakont.Agent.Installer.Tests.Fakes;

using System.Collections.Generic;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Doublure de <see cref="IAgentDeployer"/> : enregistre les plans installés et les instances désinstallées
/// (pour asserter l'isolation entre instances et le ciblage de la désinstallation), et renvoie une issue
/// configurable. Aucune E/S réelle.
/// </summary>
internal sealed class RecordingDeployer : IAgentDeployer
{
    private readonly bool _succeed;

    public RecordingDeployer(bool succeed)
    {
        _succeed = succeed;
    }

    public IList<InstallationPlan> InstalledPlans { get; } = new List<InstallationPlan>();

    public IList<string> UninstalledInstances { get; } = new List<string>();

    public DeploymentOutcome Install(InstallationPlan plan)
    {
        InstalledPlans.Add(plan);
        return new DeploymentOutcome(_succeed, new List<string> { "[OK]    déployé (doublure)" });
    }

    public DeploymentOutcome Uninstall(string instanceName)
    {
        UninstalledInstances.Add(instanceName);
        return new DeploymentOutcome(_succeed, new List<string> { "[OK]    désinstallé (doublure)" });
    }
}
