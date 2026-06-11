namespace Liakont.Host.Dashboard;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Components;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Assemble le tableau de bord d'accueil à partir des lectures Contracts (Documents, Ingestion,
/// TenantSettings). AUCUNE règle métier : compteurs, état d'agent et état de validation TVA sont
/// reportés tels quels. En particulier la cadence déclarative (opaque) est passée telle quelle —
/// aucune date d'échéance n'est calculée (règle non sourcée → R2). Tenant-scopé (CLAUDE.md n°9).
/// </summary>
internal sealed class DashboardQueryService : IDashboardQueries
{
    private readonly IDocumentQueries _documentQueries;
    private readonly IAgentQueries _agentQueries;
    private readonly ITenantSettingsConsoleQueries _settingsQueries;
    private readonly ITenantContext _tenantContext;

    public DashboardQueryService(
        IDocumentQueries documentQueries,
        IAgentQueries agentQueries,
        ITenantSettingsConsoleQueries settingsQueries,
        ITenantContext tenantContext)
    {
        _documentQueries = documentQueries;
        _agentQueries = agentQueries;
        _settingsQueries = settingsQueries;
        _tenantContext = tenantContext;
    }

    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        // Compteurs par état : la liste paginée minimale ne sert qu'à récupérer CountsByState.
        var documents = await _documentQueries
            .GetDocumentsAsync(new DocumentListFilter { Page = 1, PageSize = 1 }, cancellationToken)
            .ConfigureAwait(false);

        var stateCounts = BuildStateCounts(documents.CountsByState);
        var agents = await BuildAgentsAsync(cancellationToken).ConfigureAwait(false);

        var overview = await _settingsQueries.GetSettingsOverview(cancellationToken).ConfigureAwait(false);
        var tva = overview.TvaMapping;
        var tvaStatus = tva is null
            ? DashboardTvaStatus.NotConfigured
            : tva.IsValidated ? DashboardTvaStatus.Validated : DashboardTvaStatus.NotValidated;

        return new DashboardViewModel
        {
            ProfileConfigured = overview.Profile is not null,
            StateCounts = stateCounts,
            Agents = agents,
            TvaStatus = tvaStatus,
            TvaValidatedBy = tva?.ValidatedBy,
            TvaValidatedDate = tva?.ValidatedDate,
            ReportingFrequency = overview.FiscalSettings?.ReportingFrequency,
        };
    }

    private static List<DashboardStateCount> BuildStateCounts(IReadOnlyDictionary<string, int> counts)
    {
        var result = new List<DashboardStateCount>();

        // Ordre canonique d'abord : garantit que les états clés (À envoyer, Bloqué, Émis, Rejeté) sont
        // toujours affichés, même à zéro.
        foreach (var state in DocumentStateDisplay.CanonicalOrder)
        {
            result.Add(new DashboardStateCount(state, counts.TryGetValue(state, out var n) ? n : 0));
        }

        // Robustesse : un état non encore cartographié reste visible (jamais perdu silencieusement).
        foreach (var kvp in counts)
        {
            if (!DocumentStateDisplay.CanonicalOrder.Contains(kvp.Key))
            {
                result.Add(new DashboardStateCount(kvp.Key, kvp.Value));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<DashboardAgentLine>> BuildAgentsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return [];
        }

        // Le registre d'agents vit en base SYSTÈME : la lecture est scopée par tenantId (jamais cross-tenant).
        var agentDtos = await _agentQueries.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var agents = new List<DashboardAgentLine>(agentDtos.Count);
        foreach (var agent in agentDtos)
        {
            agents.Add(new DashboardAgentLine(agent.Name, agent.LastSeenAtUtc, agent.LastAgentVersion, agent.IsRevoked));
        }

        return agents;
    }
}
