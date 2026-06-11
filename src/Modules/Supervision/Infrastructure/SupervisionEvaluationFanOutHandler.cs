namespace Liakont.Modules.Supervision.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du dead-man's-switch (F12 §5, item SUP01a) : fait le FAN-OUT de l'évaluation de supervision sur
/// TOUS les tenants actifs via <see cref="ITenantJobRunner"/> (SOL06). C'est ICI qu'opère le principe « la
/// plateforme détecte l'absence » : un module ne planifie pas un job par tenant, il planifie UN job système
/// dont le handler appelle le runner (tenant-jobs.md §4). Les échecs par tenant sont isolés par le runner
/// et JOURNALISÉS ici (jamais avalés — un échec silencieux de supervision serait une panne silencieuse,
/// l'exact inverse du but).
/// </summary>
public sealed partial class SupervisionEvaluationFanOutHandler : IJobHandler<SupervisionEvaluationTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SupervisionEvaluationFanOutHandler> _logger;

    public SupervisionEvaluationFanOutHandler(ITenantJobRunner runner, ILogger<SupervisionEvaluationFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(SupervisionEvaluationTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SupervisionEvaluationTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogEvaluated(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Supervision : {Succeeded}/{Total} tenants évalués.")]
    private static partial void LogEvaluated(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Évaluation de supervision échouée pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}
