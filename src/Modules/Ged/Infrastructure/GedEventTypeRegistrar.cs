namespace Liakont.Modules.Ged.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Contracts.Events;
using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Enregistre, au démarrage, la correspondance type d'événement → payload CLR pour les événements publiés par le canal
/// GED (GED05b), afin que le worker d'outbox sache les désérialiser et les dispatcher (sinon il les marque « inconnus »).
/// Même motif que <c>IngestionEventTypeRegistrar</c>, mais espace de types DISJOINT (F19 §4.1).
/// </summary>
internal sealed class GedEventTypeRegistrar : IHostedService
{
    private readonly IEventTypeRegistry _registry;

    public GedEventTypeRegistrar(IEventTypeRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Register<ManagedDocumentReceivedV1>(GedEventTypes.ManagedDocumentReceived);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
