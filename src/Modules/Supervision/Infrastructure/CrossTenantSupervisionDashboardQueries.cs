namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation cross-tenant du dashboard de supervision (SUP02). Énumère les tenants actifs via le
/// registre SYSTÈME (<see cref="ITenantQueries"/>, indépendant du tenant ambiant) puis, pour chacun, ouvre
/// un scope tenant (<see cref="ITenantScopeFactory"/>) et agrège des lectures elles-mêmes tenant-scopées
/// (alertes, compteurs de documents, état des agents). C'est l'UNIQUE lecture cross-tenant du produit
/// (CLAUDE.md n°9, blueprint §7 règle 2) ; le fan-out résilient (un tenant en échec est journalisé — jamais
/// avalé — et reste VISIBLE, pas masqué) reprend <c>FanOutPortalQueryService</c>. Aucune requête maison
/// cross-tenant : chaque lecture passe par le Contract du module propriétaire dans le scope du tenant.
/// </summary>
public sealed partial class CrossTenantSupervisionDashboardQueries : ISupervisionDashboardQueries
{
    private const int MaxConcurrentTenantQueries = 8;
    private const int RecentAlertsMax = 50;

    private const string CriticalSeverity = "Critical";
    private const string WarningSeverity = "Warning";

    private const string BlockedState = "Blocked";
    private const string RejectedByPaState = "RejectedByPa";
    private const string ReadyToSendState = "ReadyToSend";

    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<CrossTenantSupervisionDashboardQueries> _logger;

    public CrossTenantSupervisionDashboardQueries(
        ITenantQueries tenantQueries,
        ITenantScopeFactory scopeFactory,
        ILogger<CrossTenantSupervisionDashboardQueries> logger)
    {
        _tenantQueries = tenantQueries;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantSupervisionRowDto>> GetInstanceOverviewAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantDto> tenants = await _tenantQueries.ListAsync(cancellationToken);
        var activeTenants = tenants.Where(t => t.IsActive).ToList();

        var rows = new List<TenantSupervisionRowDto>(activeTenants.Count);

        await Parallel.ForEachAsync(
            activeTenants,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentTenantQueries, CancellationToken = cancellationToken },
            async (tenant, innerCt) =>
            {
                TenantSupervisionRowDto row = await BuildRowAsync(tenant, innerCt);
                lock (rows)
                {
                    rows.Add(row);
                }
            });

        // Tri opérateur : ce qui demande une action en tête — lecture en échec, puis présence de critiques,
        // puis le plus d'alertes actives, puis par nom. Le superviseur voit d'abord ce qui doit être traité.
        return rows
            .OrderByDescending(r => r.ReadFailed)
            .ThenByDescending(r => r.CriticalAlertCount > 0)
            .ThenByDescending(r => r.ActiveAlertCount)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<TenantSupervisionDetailDto?> GetTenantDetailAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        TenantDto? tenant = await _tenantQueries.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            return null;
        }

        await using ITenantScope scope = _scopeFactory.Create(tenant.Id);

        IAlertQueries alertQueries = scope.Services.GetRequiredService<IAlertQueries>();
        IReadOnlyList<AlertDto> activeAlerts = await alertQueries.ListActiveAsync(cancellationToken);
        IReadOnlyList<AlertDto> recentAlerts = await alertQueries.ListRecentAsync(RecentAlertsMax, cancellationToken);

        DocumentCounts docs = await ReadDocumentCountsAsync(scope, cancellationToken);

        IReadOnlyList<AgentSummaryDto> agents = await scope.Services
            .GetRequiredService<IAgentQueries>()
            .ListByTenantAsync(tenant.Id, cancellationToken);

        return new TenantSupervisionDetailDto
        {
            TenantId = tenant.Id,
            DisplayName = tenant.DisplayName,
            Agents = agents.Select(MapAgent).ToList(),
            ActiveAlerts = activeAlerts,
            RecentAlerts = recentAlerts,
            BlockedDocumentCount = docs.Blocked,
            RejectedByPaDocumentCount = docs.RejectedByPa,
            PendingDocumentCount = docs.Pending,
        };
    }

    public async Task<bool> AcknowledgeAsync(string tenantId, Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorIdentity);

        await using ITenantScope scope = _scopeFactory.Create(tenantId);
        return await scope.Services
            .GetRequiredService<IAlertAcknowledgementService>()
            .AcknowledgeAsync(alertId, operatorIdentity, cancellationToken);
    }

    private static async Task<DocumentCounts> ReadDocumentCountsAsync(ITenantScope scope, CancellationToken ct)
    {
        // Les compteurs par état viennent de CountsByState (groupement complet, filtre d'état ignoré) :
        // une page d'1 ligne suffit, on ne lit pas la file entière pour compter.
        DocumentListResult docList = await scope.Services
            .GetRequiredService<IDocumentQueries>()
            .GetDocumentsAsync(new DocumentListFilter { Page = 1, PageSize = 1 }, ct);

        IReadOnlyDictionary<string, int> counts = docList.CountsByState;
        return new DocumentCounts(
            Blocked: counts.TryGetValue(BlockedState, out int blocked) ? blocked : 0,
            RejectedByPa: counts.TryGetValue(RejectedByPaState, out int rejected) ? rejected : 0,
            Pending: counts.TryGetValue(ReadyToSendState, out int pending) ? pending : 0);
    }

    private static string? WorstSeverity(IReadOnlyList<AlertDto> activeAlerts)
    {
        if (activeAlerts.Any(a => string.Equals(a.Severity, CriticalSeverity, StringComparison.Ordinal)))
        {
            return CriticalSeverity;
        }

        if (activeAlerts.Any(a => string.Equals(a.Severity, WarningSeverity, StringComparison.Ordinal)))
        {
            return WarningSeverity;
        }

        return null;
    }

    private static AgentStatusDto MapAgent(AgentSummaryDto agent) => new()
    {
        Name = agent.Name,
        IsRevoked = agent.IsRevoked,
        LastSeenAtUtc = agent.LastSeenAtUtc,
        LastAgentVersion = agent.LastAgentVersion,
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Supervision : lecture du tenant {TenantId} échouée — affiché en avertissement (compteurs à zéro), jamais masqué.")]
    private static partial void LogTenantReadFailed(ILogger logger, string tenantId, Exception exception);

    private async Task<TenantSupervisionRowDto> BuildRowAsync(TenantDto tenant, CancellationToken ct)
    {
        try
        {
            await using ITenantScope scope = _scopeFactory.Create(tenant.Id);

            IReadOnlyList<AlertDto> activeAlerts = await scope.Services
                .GetRequiredService<IAlertQueries>()
                .ListActiveAsync(ct);

            DocumentCounts docs = await ReadDocumentCountsAsync(scope, ct);

            IReadOnlyList<AgentSummaryDto> agents = await scope.Services
                .GetRequiredService<IAgentQueries>()
                .ListByTenantAsync(tenant.Id, ct);

            int criticalCount = activeAlerts.Count(a => string.Equals(a.Severity, CriticalSeverity, StringComparison.Ordinal));

            return new TenantSupervisionRowDto
            {
                TenantId = tenant.Id,
                DisplayName = tenant.DisplayName,
                ActiveAlertCount = activeAlerts.Count,
                CriticalAlertCount = criticalCount,
                WorstSeverity = WorstSeverity(activeAlerts),
                AgentCount = agents.Count,
                LastAgentSeenUtc = agents.Max(a => a.LastSeenAtUtc),
                BlockedDocumentCount = docs.Blocked,
                RejectedByPaDocumentCount = docs.RejectedByPa,
                PendingDocumentCount = docs.Pending,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un tenant injoignable reste VISIBLE (ReadFailed) — jamais masqué : un tenant absent du tableau
            // serait la panne silencieuse que la supervision existe pour détecter. L'échec est journalisé.
            LogTenantReadFailed(_logger, tenant.Id, ex);
            return new TenantSupervisionRowDto
            {
                TenantId = tenant.Id,
                DisplayName = tenant.DisplayName,
                ActiveAlertCount = 0,
                CriticalAlertCount = 0,
                WorstSeverity = null,
                AgentCount = 0,
                LastAgentSeenUtc = null,
                BlockedDocumentCount = 0,
                RejectedByPaDocumentCount = 0,
                PendingDocumentCount = 0,
                ReadFailed = true,
            };
        }
    }

    private readonly record struct DocumentCounts(int Blocked, int RejectedByPa, int Pending);
}
