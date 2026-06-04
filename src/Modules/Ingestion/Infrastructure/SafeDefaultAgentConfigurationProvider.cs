namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Application;

/// <summary>
/// Configuration d'agent par DÉFAUT SÛR (F12 §2.5, §3.2, décision D6) : aucune mise à jour imposée
/// et aucune planification poussée tant que leurs sources n'existent pas.
/// <list type="bullet">
///   <item><c>UpdateRequired = false</c> et champs d'update <c>null</c> : le registre de versions
///   (OPS07) n'existe pas encore — comportement sûr documenté dans <see cref="AgentConfigurationDto"/>.</item>
///   <item><c>ExtractionSchedule = null</c> : la planification pilotée par le tenant (priorité
///   plateforme, F12 décision D3) n'est pas encore branchée ; <c>null</c> fait que l'agent retombe
///   sur sa planification LOCALE (repli sanctionné par F12 §2.4). Sa dérivation depuis le
///   paramétrage tenant exige une règle sourcée — non inventée ici (CLAUDE.md n°2).</item>
/// </list>
/// C'est le point d'extension unique : OPS07 et la planification pilotée se brancheront ici, en
/// remplaçant cette implémentation, sans toucher aux endpoints ni aux handlers.
/// </summary>
internal sealed class SafeDefaultAgentConfigurationProvider : IAgentConfigurationProvider
{
    public Task<AgentConfigurationDto> GetForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentConfigurationDto());
    }
}
