namespace Liakont.Host.Backfill;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME de backfill rétroactif GED (GED10) : fan-out de <see cref="GedCorpusBackfillTenantJob"/>
/// sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/> (SOL06, patron <c>DailyAnchoring</c>). Les échecs
/// par tenant sont isolés par le runner et journalisés ici (jamais avalés silencieusement). Câblé au composition root
/// (<c>AddJobHandler</c>, extension du module Job que seul le Host référence).
/// </summary>
public sealed partial class GedCorpusBackfillFanOutHandler : IJobHandler<GedCorpusBackfillTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<GedCorpusBackfillFanOutHandler> _logger;

    public GedCorpusBackfillFanOutHandler(ITenantJobRunner runner, ILogger<GedCorpusBackfillFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(GedCorpusBackfillTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new GedCorpusBackfillTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogBackfilled(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backfill GED du corpus fiscal : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogBackfilled(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backfill GED du corpus fiscal échoué pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}
