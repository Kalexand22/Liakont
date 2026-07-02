namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Contracts.Events;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Contribue, AU BUILD DI, la correspondance type d'événement → payload CLR pour les événements
/// publiés par l'ingestion (PIV04), afin que le worker d'outbox sache les désérialiser et les
/// dispatcher (sinon il les marque « inconnus »). Enregistré comme <see cref="IEventTypeRegistrar"/>
/// (appliqué à la construction du registre par <c>AddStratumEvents</c>), donc AVANT le premier poll de
/// l'OutboxWorker (GDF01 : l'ancien motif <c>IHostedService</c> enregistrait trop tard, en concurrence
/// avec le worker → un événement pendant au redémarrage pouvait être marqué processed à vide).
/// </summary>
internal sealed class IngestionEventTypeRegistrar : IEventTypeRegistrar
{
    public void Register(IEventTypeRegistry registry)
    {
        registry
            .Register<DocumentReceivedV1>(IngestionEventTypes.DocumentReceived)
            .Register<SourceAlterationDetectedV1>(IngestionEventTypes.SourceAlterationDetected);
    }
}
