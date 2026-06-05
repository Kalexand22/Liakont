namespace Liakont.Agent.Core.Tests.Update;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Update;

/// <summary>
/// Service d'auto-update pilotable : capture les configurations examinées et les signaux 426, expose
/// un statut scriptable. Sert à vérifier le CÂBLAGE des déclencheurs (heartbeat + 426) sans exécuter
/// la vraie mécanique de mise à jour.
/// </summary>
internal sealed class FakeAutoUpdateService : IAutoUpdateService
{
    public List<AgentConfigurationDto> ConsideredConfigurations { get; } = new List<AgentConfigurationDto>();

    public int PushUpgradeSignals { get; private set; }

    public AutoUpdateStatus? LatestStatus { get; set; }

    public AutoUpdateResult ResultToReturn { get; set; } = new AutoUpdateResult(AutoUpdateOutcome.NotRequested, "stub");

    public AutoUpdateResult ConsiderHeartbeatConfiguration(AgentConfigurationDto configuration)
    {
        ConsideredConfigurations.Add(configuration);
        return ResultToReturn;
    }

    public void RecordPushUpgradeRequired() => PushUpgradeSignals++;

    public AutoUpdateStatus? GetLatestStatus() => LatestStatus;
}
