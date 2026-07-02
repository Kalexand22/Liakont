namespace Liakont.Modules.Ingestion.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Ingestion.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Events;
using Stratum.Common.Infrastructure.Outbox;
using Xunit;

/// <summary>
/// GDF01 — le canal Ingestion est couvert par le MÊME mécanisme que GED : ses types d'événements sont
/// enregistrés à la CONSTRUCTION du registre (au build DI, via <see cref="IEventTypeRegistrar"/>), donc AVANT
/// tout poll de l'OutboxWorker. Aucune base requise : on ne résout que le registre, sans démarrer de hosted
/// service — prouve que <c>AddIngestionModule</c> contribue bien ses types au build (plus d'enregistrement
/// tardif via <c>IHostedService</c> en concurrence avec le worker).
/// </summary>
public sealed class IngestionEventTypeRegistrarMechanismTests
{
    [Fact]
    public void Ingestion_event_types_are_registered_at_build_before_any_poll()
    {
        var services = new ServiceCollection();
        services.AddStratumEvents();
        services.AddIngestionModule();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEventTypeRegistry>();

        registry.GetPayloadType(IngestionEventTypes.DocumentReceived).Should().Be<DocumentReceivedV1>(
            "le type ingestion.document.received est contribué à la construction du registre (GDF01)");
        registry.GetPayloadType(IngestionEventTypes.SourceAlterationDetected).Should().Be<SourceAlterationDetectedV1>(
            "le type ingestion.source.altered est contribué à la construction du registre (GDF01)");
    }
}
