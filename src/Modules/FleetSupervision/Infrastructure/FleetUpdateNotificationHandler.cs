namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job de notification de mise à jour (OPS04, rôle CENTRAL) : notifie par email les instances
/// self-hosted EN RETARD sur la dernière version publiée. Anti-rebond : on n'envoie qu'une fois par version
/// (la version notifiée est mémorisée). No-op journalisé si le rôle central est désactivé ou si aucune
/// version publiée n'est configurée.
/// </summary>
public sealed partial class FleetUpdateNotificationHandler : IJobHandler<FleetUpdateNotificationTrigger>
{
    private readonly IFleetInstanceStore _store;
    private readonly IFleetUpdateNotificationSender _sender;
    private readonly IOptions<FleetSupervisionOptions> _options;
    private readonly ILogger<FleetUpdateNotificationHandler> _logger;

    public FleetUpdateNotificationHandler(
        IFleetInstanceStore store,
        IFleetUpdateNotificationSender sender,
        IOptions<FleetSupervisionOptions> options,
        ILogger<FleetUpdateNotificationHandler> logger)
    {
        _store = store;
        _sender = sender;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(FleetUpdateNotificationTrigger payload, CancellationToken ct = default)
    {
        FleetCentralOptions central = _options.Value.Central;
        if (!central.Enabled)
        {
            LogDisabled(_logger);
            return;
        }

        if (string.IsNullOrWhiteSpace(central.LatestVersion))
        {
            LogNoLatestVersion(_logger);
            return;
        }

        IReadOnlyList<FleetNotificationCandidate> candidates =
            await _store.ListNotificationCandidatesAsync(ct).ConfigureAwait(false);

        int notified = 0;
        foreach (FleetNotificationCandidate candidate in candidates)
        {
            if (!FleetVersion.IsObsolete(candidate.Version, central.LatestVersion))
            {
                continue;
            }

            // Anti-rebond : déjà notifiée pour CETTE version publiée → on ne renvoie pas.
            if (string.Equals(candidate.NotifiedVersion, central.LatestVersion, StringComparison.Ordinal))
            {
                continue;
            }

            await _sender.SendNewVersionAvailableAsync(
                candidate.ContactEmail,
                candidate.DisplayName,
                candidate.Version,
                central.LatestVersion,
                ct).ConfigureAwait(false);

            await _store.MarkNotifiedAsync(candidate.InstanceId, central.LatestVersion, ct).ConfigureAwait(false);
            notified++;
        }

        LogNotified(_logger, notified, central.LatestVersion);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Méta-supervision : rôle central désactivé, aucune notification de mise à jour.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Méta-supervision : rôle central activé sans dernière version publiée (FleetSupervision:Central:LatestVersion) — aucune notification.")]
    private static partial void LogNoLatestVersion(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Méta-supervision : {Count} instance(s) self-hosted notifiée(s) de la version {LatestVersion}.")]
    private static partial void LogNotified(ILogger logger, int count, string latestVersion);
}
