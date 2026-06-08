namespace Liakont.Host.Parametrage;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Assemble la page Paramétrage du tenant (WEB04b) à partir des lectures Contracts (TenantSettings
/// via <c>GET /settings</c> + registre d'agents) et délègue la vérification d'intégrité du coffre au
/// vérifieur d'archive (TRK06). AUCUNE règle métier : les paramètres fiscaux opaques et les capacités
/// PA sont reportés tels quels (aucune valeur devinée — CLAUDE.md n°2). Tenant-scopé (CLAUDE.md n°9).
/// </summary>
internal sealed class ParametrageQueryService : IParametrageQueries
{
    private readonly ITenantSettingsConsoleQueries _settingsQueries;
    private readonly IAgentQueries _agentQueries;
    private readonly IArchiveVerifier _archiveVerifier;
    private readonly ITenantContext _tenantContext;

    public ParametrageQueryService(
        ITenantSettingsConsoleQueries settingsQueries,
        IAgentQueries agentQueries,
        IArchiveVerifier archiveVerifier,
        ITenantContext tenantContext)
    {
        _settingsQueries = settingsQueries;
        _agentQueries = agentQueries;
        _archiveVerifier = archiveVerifier;
        _tenantContext = tenantContext;
    }

    public async Task<ParametrageViewModel> GetParametrageAsync(CancellationToken cancellationToken = default)
    {
        var overview = await _settingsQueries.GetSettingsOverview(cancellationToken).ConfigureAwait(false);
        var agents = await BuildAgentsAsync(cancellationToken).ConfigureAwait(false);

        return new ParametrageViewModel
        {
            Profile = overview.Profile,
            FiscalSettings = overview.FiscalSettings,
            TvaMapping = overview.TvaMapping,
            PaAccounts = overview.PaAccounts,
            Agents = agents,
        };
    }

    public Task<ArchiveVerificationReport> VerifyArchiveIntegrityAsync(CancellationToken cancellationToken = default) =>
        _archiveVerifier.VerifyTenantVaultAsync(cancellationToken);

    private async Task<IReadOnlyList<ParametrageAgentLine>> BuildAgentsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return [];
        }

        // Le registre d'agents vit en base SYSTÈME : la lecture est scopée par tenantId (jamais cross-tenant).
        var agentDtos = await _agentQueries.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var agents = new List<ParametrageAgentLine>(agentDtos.Count);
        foreach (var agent in agentDtos)
        {
            agents.Add(new ParametrageAgentLine(agent.Name, agent.LastSeenAtUtc, agent.LastAgentVersion, agent.IsRevoked));
        }

        return agents;
    }
}
