namespace Liakont.Modules.Ingestion.Application;

using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Construit la configuration renvoyée à un agent (heartbeat et GET /configuration, F12 §3.2).
/// C'est le point d'extension unique pour brancher, à terme, la planification pilotée par le tenant
/// (priorité plateforme, F12 décision D3) et la politique de mise à jour de flotte alimentée par le
/// registre de versions (OPS07). Tant que ces sources n'existent pas, l'implémentation renvoie un
/// défaut SÛR (aucune mise à jour imposée, repli de l'agent sur sa planification locale).
/// </summary>
public interface IAgentConfigurationProvider
{
    Task<AgentConfigurationDto> GetForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
