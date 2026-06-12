namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job d'envoi de télémétrie d'instance (OPS04, rôle REPORTING) : collecte la télémétrie technique
/// locale et la publie au central. No-op journalisé si le reporting est désactivé ou si l'identifiant
/// d'instance n'est pas configuré (opt-in par déploiement). La publication est non bloquante (cf.
/// <see cref="IFleetReportPublisher"/>).
/// </summary>
public sealed partial class InstanceHeartbeatSendHandler : IJobHandler<InstanceHeartbeatTrigger>
{
    private readonly IInstanceTelemetryCollector _collector;
    private readonly IFleetReportPublisher _publisher;
    private readonly IOptions<FleetSupervisionOptions> _options;
    private readonly ILogger<InstanceHeartbeatSendHandler> _logger;

    public InstanceHeartbeatSendHandler(
        IInstanceTelemetryCollector collector,
        IFleetReportPublisher publisher,
        IOptions<FleetSupervisionOptions> options,
        ILogger<InstanceHeartbeatSendHandler> logger)
    {
        _collector = collector;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(InstanceHeartbeatTrigger payload, CancellationToken ct = default)
    {
        FleetReportingOptions reporting = _options.Value.Reporting;
        if (!reporting.Enabled)
        {
            LogDisabled(_logger);
            return;
        }

        if (string.IsNullOrWhiteSpace(reporting.InstanceId))
        {
            LogNoInstanceId(_logger);
            return;
        }

        InstanceHeartbeatReport report = await _collector.CollectAsync(ct).ConfigureAwait(false);
        await _publisher.PublishAsync(report, ct).ConfigureAwait(false);
        LogSent(_logger, reporting.InstanceId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Méta-supervision : reporting désactivé, télémétrie non envoyée.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Méta-supervision : reporting activé sans identifiant d'instance (FleetSupervision:Reporting:InstanceId) — télémétrie non envoyée.")]
    private static partial void LogNoInstanceId(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Méta-supervision : télémétrie de l'instance {InstanceId} envoyée au central.")]
    private static partial void LogSent(ILogger logger, string instanceId);
}
