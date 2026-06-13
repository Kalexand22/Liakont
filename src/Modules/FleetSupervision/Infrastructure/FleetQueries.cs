namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Microsoft.Extensions.Options;

/// <summary>
/// Lecture de la flotte pour le dashboard (OPS04) : liste le parc depuis le magasin système puis calcule les
/// alertes (instance muette / sauvegarde en échec / version obsolète) via <see cref="FleetAlertEvaluator"/>
/// avec les seuils et la dernière version publiée du central. Lecture seule, aucun secret exposé.
/// </summary>
internal sealed class FleetQueries : IFleetQueries
{
    private readonly IFleetInstanceStore _store;
    private readonly IOptions<FleetSupervisionOptions> _options;
    private readonly TimeProvider _clock;

    public FleetQueries(IFleetInstanceStore store, IOptions<FleetSupervisionOptions> options, TimeProvider clock)
    {
        _store = store;
        _options = options;
        _clock = clock;
    }

    public async Task<FleetOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FleetInstanceDto> instances = await _store.ListAsync(cancellationToken).ConfigureAwait(false);

        FleetCentralOptions central = _options.Value.Central;
        var thresholds = new FleetAlertThresholds(central.InstanceMuteThresholdMinutes, central.BackupMaxAgeHours);
        DateTimeOffset now = _clock.GetUtcNow();

        IReadOnlyList<FleetAlertDto> alerts = FleetAlertEvaluator.EvaluateAll(instances, thresholds, central.LatestVersion, now);

        return new FleetOverviewDto
        {
            LatestVersion = central.LatestVersion,
            Instances = instances,
            Alerts = alerts,
            GeneratedUtc = now,
        };
    }
}
