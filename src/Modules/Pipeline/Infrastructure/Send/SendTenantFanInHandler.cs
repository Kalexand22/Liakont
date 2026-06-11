namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler SYSTÈME du déclencheur MONO-TENANT <see cref="SendTenantTrigger"/> (API02a, ADR-0016). À la
/// différence de <see cref="SendAllFanOutHandler"/> (fan-out tous-tenants via <c>ITenantJobRunner</c>, réservé
/// au cron d'instance), il rétablit le SEUL tenant cible (<see cref="SendTenantTrigger.TenantId"/>) via le seul
/// établisseur de tenant sanctionné hors middleware — <see cref="ITenantScopeFactory.Create(string)"/> (SOL06,
/// ADR-0006) — puis exécute <see cref="SendTenantJob"/> dans ce scope. Il n'itère JAMAIS sur les tenants
/// (INV-API02a-1/-4) : une action de console agit pour le seul tenant de l'opérateur (CLAUDE.md n°9).
/// <para>
/// Le job est publié sur la queue SYSTÈME (consommée par le <c>JobWorker</c> null-tenant) ; ce handler s'exécute
/// dans ce scope système et y crée lui-même le scope tenant — aucune couche métier ne mute le contexte de
/// tenant (INV-API02a-3). Même patron que <see cref="SendAllFanOutHandler"/>, mais ciblé sur un tenant.
/// </para>
/// </summary>
public sealed partial class SendTenantFanInHandler : IJobHandler<SendTenantTrigger>
{
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<SendTenantFanInHandler> _logger;

    /// <summary>Construit le handler d'envoi mono-tenant.</summary>
    /// <param name="scopeFactory">La fabrique de scope tenant du socle (Host) — bascule vers le tenant cible.</param>
    /// <param name="logger">Le journal applicatif.</param>
    public SendTenantFanInHandler(ITenantScopeFactory scopeFactory, ILogger<SendTenantFanInHandler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(SendTenantTrigger payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Bascule vers le SEUL tenant cible (jamais un fan-out) : le scope route la connexion vers SA base.
        await using ITenantScope scope = _scopeFactory.Create(payload.TenantId);

        // Déclenchement MANUEL (opérateur via la console / l'API). Le mode simulation est porté par la charge utile.
        var job = new SendTenantJob(PipelineRunTrigger.Manual, payload.DryRun);
        var context = new TenantJobContext(scope.TenantId, scope.Services);
        await job.ExecuteAsync(context, ct);

        LogTenantSendCompleted(_logger, payload.TenantId, payload.DryRun);
    }

    [LoggerMessage(EventId = 7230, Level = LogLevel.Information,
        Message = "SEND manuel (console) terminé pour le tenant {TenantId} (simulation : {DryRun}).")]
    private static partial void LogTenantSendCompleted(ILogger logger, string tenantId, bool dryRun);
}
