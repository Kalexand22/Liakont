namespace Liakont.Agent;

using System.ServiceProcess;

/// <summary>
/// Service Windows hôte de l'agent : planification locale, push HTTPS, heartbeat
/// (blueprint.md §3.1). Coquille : la logique (extraction, buffer, transport) arrive avec
/// les items AGT. Aucune logique métier ici (blueprint.md §6).
/// </summary>
internal sealed class AgentService : ServiceBase
{
    public AgentService()
    {
        ServiceName = "LiakontAgent";
    }

    /// <inheritdoc />
    protected override void OnStart(string[] args)
    {
        // La boucle de planification locale est câblée par les items AGT.
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        // L'arrêt propre (flush du buffer local) est câblé par les items AGT.
    }
}
