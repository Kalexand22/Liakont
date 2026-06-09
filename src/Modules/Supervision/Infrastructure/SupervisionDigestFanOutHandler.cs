namespace Liakont.Modules.Supervision.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du récapitulatif quotidien (digest) des alertes actives (SUP03 §3, OPTIONNEL) : fait le FAN-OUT
/// du digest sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/> (SOL06), comme le dead-man's-switch
/// (<see cref="SupervisionEvaluationFanOutHandler"/>). Un module ne planifie pas un job par tenant : il planifie
/// UN job système dont le handler appelle le runner (tenant-jobs.md §4). Les échecs par tenant sont isolés par
/// le runner et JOURNALISÉS ici.
/// <para>Si le digest est désactivé au niveau instance (<c>DailyDigestEnabled=false</c>), on court-circuite le
/// fan-out AVANT de créer un scope par tenant (le garde par tenant dans le sender reste, en défense en
/// profondeur). Quand il est activé, l'opérateur reçoit UN digest PAR TENANT ayant des alertes actives (pas un
/// agrégat consolidé) : c'est le pendant naturel du fan-out par tenant du dead-man's-switch — une lecture
/// cross-tenant consolidée n'est pas introduite ici.</para>
/// </summary>
public sealed partial class SupervisionDigestFanOutHandler : IJobHandler<SupervisionDigestTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly SupervisionNotificationOptions _options;
    private readonly ILogger<SupervisionDigestFanOutHandler> _logger;

    public SupervisionDigestFanOutHandler(
        ITenantJobRunner runner,
        IOptions<SupervisionNotificationOptions> options,
        ILogger<SupervisionDigestFanOutHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(SupervisionDigestTrigger payload, CancellationToken ct = default)
    {
        if (!_options.DailyDigestEnabled)
        {
            // Désactivé : aucun fan-out (pas de scope par tenant pour ne rien faire).
            LogDisabled(_logger);
            return;
        }

        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SupervisionDigestTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogDigested(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Digest de supervision désactivé (DailyDigestEnabled=false) : fan-out ignoré.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Supervision (digest) : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogDigested(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Digest de supervision échoué pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}
