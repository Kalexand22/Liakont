namespace Liakont.Modules.Signature.Infrastructure.Drain;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME de drain des webhooks de signature (ADR-0029 §5) : fait le fan-out de
/// <see cref="SignatureWebhookDrainJob"/> sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/>
/// (SOL06). Les échecs par tenant sont isolés par le runner (l'échec d'un tenant n'affecte pas les autres) ;
/// ils sont journalisés ici (jamais avalés silencieusement) pour alerte/supervision. Enregistré côté Host
/// (AddJobHandler).
/// </summary>
public sealed partial class SignatureWebhookDrainFanOutHandler : IJobHandler<SignatureWebhookDrainTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SignatureWebhookDrainFanOutHandler> _logger;

    public SignatureWebhookDrainFanOutHandler(
        ITenantJobRunner runner, ILogger<SignatureWebhookDrainFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(SignatureWebhookDrainTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SignatureWebhookDrainJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogProcessed(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Drain des webhooks de signature : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogProcessed(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drain des webhooks de signature échoué pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}
