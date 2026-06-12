namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Domain;

/// <summary>
/// Réception d'un heartbeat d'instance côté CENTRAL (OPS04) : valide/normalise via
/// <see cref="FleetInstance.Register"/> et upsert dans le magasin système. Horloge partagée
/// (<see cref="TimeProvider"/>) pour un horodatage déterministe en test.
/// </summary>
internal sealed class FleetHeartbeatIngestor : IFleetHeartbeatIngestor
{
    private readonly IFleetInstanceStore _store;
    private readonly TimeProvider _clock;

    public FleetHeartbeatIngestor(IFleetInstanceStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task RecordAsync(InstanceHeartbeatReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        FleetInstance instance = FleetInstance.Register(report, _clock.GetUtcNow());
        await _store.UpsertAsync(instance, cancellationToken).ConfigureAwait(false);
    }
}
