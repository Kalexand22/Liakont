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
    private readonly IDocumentStateCountQueries _documentQueries;
    private readonly IAgentQueries _agentQueries;
    private readonly ITenantSettingsConsoleQueries _settingsQueries;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public DashboardQueryService(
        IDocumentStateCountQueries documentQueries,
        IAgentQueries agentQueries,
        ITenantSettingsConsoleQueries settingsQueries,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _documentQueries = documentQueries;
        _agentQueries = agentQueries;
        _settingsQueries = settingsQueries;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        // Trois périmètres de compteurs, aux bornes EXPLICITES portées par le modèle : le drill-down
        // d'une tuile/barre rouvre la liste sur exactement le périmètre compté (mêmes bornes des deux
        // côtés — sans cela, la page Documents s'ouvre sur son défaut « mois courant » et peut montrer
        // moins de documents que le compteur cliqué).
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        var currentMonth = await BuildScopeAsync(
            "current-month", "Mois en cours", firstOfMonth, firstOfMonth.AddMonths(1).AddDays(-1), cancellationToken).ConfigureAwait(false);
        var previousMonth = await BuildScopeAsync(
            "previous-month", "Mois précédent", firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1), cancellationToken).ConfigureAwait(false);
        var currentYear = await BuildScopeAsync(
            "current-year", "Année en cours", new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), cancellationToken).ConfigureAwait(false);

        var agents = await BuildAgentsAsync(cancellationToken).ConfigureAwait(false);

        var overview = await _settingsQueries.GetSettingsOverview(cancellationToken).ConfigureAwait(false);
        var tva = overview.TvaMapping;
        var tvaStatus = tva is null
            ? DashboardTvaStatus.NotConfigured
            : tva.IsValidated ? DashboardTvaStatus.Validated : DashboardTvaStatus.NotValidated;

        return new DashboardViewModel
        {
            ProfileConfigured = overview.Profile is not null,
            CurrentMonth = currentMonth,
            PreviousMonth = previousMonth,
            CurrentYear = currentYear,
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

    /// <summary>
    /// Compteurs par état sur [<paramref name="from"/>, <paramref name="to"/>], via la lecture
    /// dédiée aux synthèses (seule la requête de répartition est exécutée — pas de liste ni de
    /// total calculés pour rien).
    /// </summary>
    private async Task<DashboardCounterScope> BuildScopeAsync(
        string key, string label, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var counts = await _documentQueries
            .GetStateCountsAsync(new DocumentListFilter { From = from, To = to }, cancellationToken)
            .ConfigureAwait(false);

        return new DashboardCounterScope
        {
            Key = key,
            Label = label,
            From = from,
            To = to,
            Counts = BuildStateCounts(counts),
        };
    }

    private async Task<IReadOnlyList<AgentStatusLine>> BuildAgentsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return [];
        }

        // Le registre d'agents vit en base SYSTÈME : la lecture est scopée par tenantId (jamais cross-tenant).
        var agentDtos = await _agentQueries.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var agents = new List<AgentStatusLine>(agentDtos.Count);
        foreach (var agent in agentDtos)
        {
            agents.Add(new AgentStatusLine(agent.Name, agent.LastSeenAtUtc, agent.LastAgentVersion, agent.IsRevoked));
        }

        return agents;
    }
}
