namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Contracts.Events;
using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Enregistre, au démarrage, la correspondance type d'événement → payload CLR pour les événements
/// publiés par l'ingestion (PIV04), afin que le worker d'outbox sache les désérialiser et les
/// dispatcher (sinon il les marque « inconnus »). Même motif que <c>IdentityEventTypeRegistrar</c>.
/// </summary>
internal sealed class IngestionEventTypeRegistrar : IHostedService
{
    private readonly IEventTypeRegistry _registry;

    public IngestionEventTypeRegistrar(IEventTypeRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry
            .Register<DocumentReceivedV1>(IngestionEventTypes.DocumentReceived)
            .Register<SourceAlterationDetectedV1>(IngestionEventTypes.SourceAlterationDetected);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
