namespace Liakont.Agent;

using System;
using System.ServiceProcess;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Time;

/// <summary>
/// Service Windows hôte de l'agent (F12 §2, blueprint.md §6) : démarre l'hôte de fond et garantit
/// l'arrêt PROPRE (un run en cours se termine avant l'arrêt). Aucune logique métier ici — extraction,
/// buffer, transport et heartbeat sont câblés par AGT02/AGT03 derrière l'hôte.
/// </summary>
internal sealed class AgentService : ServiceBase
{
    private const int GraceSeconds = 90;

    private AgentHost? _host;
    private FileAgentLog? _log;

    public AgentService()
    {
        ServiceName = "LiakontAgent";
        CanStop = true;
        CanShutdown = true;
    }

    /// <inheritdoc />
    protected override void OnStart(string[] args)
    {
        _log = new FileAgentLog(AgentPaths.LogDirectory, new SystemClock());
        _log.Info("Service Liakont Agent — démarrage.");
        _host = AgentHost.Create(_log);
        _host.Start();
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        _log?.Info("Service Liakont Agent — arrêt demandé, attente de la fin du run en cours.");

        // Prévenir le SCM que l'arrêt peut prendre du temps (attente d'un run d'extraction en cours).
        RequestAdditionalTime((GraceSeconds + 5) * 1000);

        bool idle = _host?.Stop(TimeSpan.FromSeconds(GraceSeconds)) ?? true;
        if (!idle)
        {
            _log?.Warn("Service Liakont Agent — délai d'arrêt dépassé, un run était encore en cours.");
        }

        _log?.Info("Service Liakont Agent — arrêté.");
    }

    /// <inheritdoc />
    protected override void OnShutdown()
    {
        OnStop();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host?.Dispose();
        }

        base.Dispose(disposing);
    }
}
