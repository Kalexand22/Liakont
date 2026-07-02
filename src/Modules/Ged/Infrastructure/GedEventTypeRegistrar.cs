namespace Liakont.Modules.Ged.Infrastructure;

using Liakont.Modules.Ged.Contracts.Events;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Contribue, AU BUILD DI, la correspondance type d'événement → payload CLR pour les événements publiés par le canal
/// GED (GED05b), afin que le worker d'outbox sache les désérialiser et les dispatcher (sinon il les marque « inconnus »).
/// Enregistré comme <see cref="IEventTypeRegistrar"/> (appliqué à la construction du registre par <c>AddStratumEvents</c>),
/// donc AVANT le premier poll de l'OutboxWorker (GDF01 : l'ancien motif <c>IHostedService</c> enregistrait trop tard, en
/// concurrence avec le worker → événement pendant marqué processed à vide). Même motif que <c>IngestionEventTypeRegistrar</c>,
/// mais espace de types DISJOINT (F19 §4.1).
/// </summary>
internal sealed class GedEventTypeRegistrar : IEventTypeRegistrar
{
    public void Register(IEventTypeRegistry registry)
    {
        registry.Register<ManagedDocumentReceivedV1>(GedEventTypes.ManagedDocumentReceived);
    }
}
