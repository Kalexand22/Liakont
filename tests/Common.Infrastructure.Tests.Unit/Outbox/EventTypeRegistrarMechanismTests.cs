namespace Stratum.Common.Infrastructure.Tests.Unit.Outbox;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Events;
using Stratum.Common.Infrastructure.Outbox;
using Xunit;

/// <summary>
/// GDF01 — mécanisme de peuplement du registre de types d'événements outbox. <c>AddStratumEvents</c>
/// construit <see cref="IEventTypeRegistry"/> en appliquant TOUS les <see cref="IEventTypeRegistrar"/>
/// enregistrés à la CONSTRUCTION du singleton (au build DI). Ces tests prouvent, SANS démarrer le moindre
/// hosted service, que dès qu'<see cref="IEventTypeRegistry"/> est résolu, les types contribués sont connus :
/// aucun poll de l'OutboxWorker ne peut donc précéder l'enregistrement des types (course de démarrage
/// supprimée — un événement pendant n'est plus vu « inconnu » puis marqué processed à vide).
/// </summary>
public sealed class EventTypeRegistrarMechanismTests
{
    [Fact]
    public void Resolved_registry_already_knows_contributor_types_without_starting_hosted_services()
    {
        var services = new ServiceCollection();
        services.AddStratumEvents();
        services.AddSingleton<IEventTypeRegistrar, FakeRegistrar>();

        using var provider = services.BuildServiceProvider();

        // On ne résout QUE le registre : aucun IHostedService (dont l'OutboxWorker) n'est démarré.
        var registry = provider.GetRequiredService<IEventTypeRegistry>();

        registry.GetPayloadType("test.gdf01.fake").Should().Be<FakePayload>(
            "le contributeur est appliqué à la construction du registre — avant tout poll de l'OutboxWorker");
    }

    [Fact]
    public void All_registered_contributors_are_applied_at_construction()
    {
        var services = new ServiceCollection();
        services.AddStratumEvents();
        services.AddSingleton<IEventTypeRegistrar, FakeRegistrar>();
        services.AddSingleton<IEventTypeRegistrar, SecondRegistrar>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEventTypeRegistry>();

        registry.GetPayloadType("test.gdf01.fake").Should().Be<FakePayload>();
        registry.GetPayloadType("test.gdf01.second").Should().Be<SecondPayload>(
            "plusieurs modules contribuent au même registre à la construction");
    }

    [Fact]
    public void Unknown_event_type_still_returns_null()
    {
        var services = new ServiceCollection();
        services.AddStratumEvents();
        services.AddSingleton<IEventTypeRegistrar, FakeRegistrar>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEventTypeRegistry>();

        registry.GetPayloadType("test.gdf01.unregistered").Should().BeNull(
            "un type non contribué reste inconnu (comportement inchangé : le worker le marquera processed pour éviter un re-poll infini)");
    }

    private sealed record FakePayload;

    private sealed record SecondPayload;

    private sealed class FakeRegistrar : IEventTypeRegistrar
    {
        public void Register(IEventTypeRegistry registry) => registry.Register<FakePayload>("test.gdf01.fake");
    }

    private sealed class SecondRegistrar : IEventTypeRegistrar
    {
        public void Register(IEventTypeRegistry registry) => registry.Register<SecondPayload>("test.gdf01.second");
    }
}
