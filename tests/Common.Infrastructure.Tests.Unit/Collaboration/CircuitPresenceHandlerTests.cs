namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Collaboration;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class CircuitPresenceHandlerTests
{
    [Fact]
    public async Task OnConnectionDown_Should_UntrackAllRegisteredCircuits()
    {
        var collaborationService = new CollaborationService();
        var registry = new CircuitPresenceRegistry();
        var handler = new CircuitPresenceHandler(
            collaborationService, registry, NullLogger<CircuitPresenceHandler>.Instance);

        // Register two virtual circuits
        registry.Register("vc-001");
        registry.Register("vc-002");

        // Track presence via both
        collaborationService.Track("Quote", "42", "vc-001", "alice");
        collaborationService.Track("Quote", "42", "vc-002", "bob");

        collaborationService.GetPresence("Quote", "42").Should().HaveCount(2);

        // Simulate disconnect
        await handler.OnConnectionDownAsync(CreateCircuit(), CancellationToken.None);

        collaborationService.GetPresence("Quote", "42").Should().BeEmpty();
    }

    [Fact]
    public async Task OnConnectionDown_Should_ClearFieldFocusForAllCircuits()
    {
        var collaborationService = new CollaborationService();
        var registry = new CircuitPresenceRegistry();
        var handler = new CircuitPresenceHandler(
            collaborationService, registry, NullLogger<CircuitPresenceHandler>.Instance);

        registry.Register("vc-001");

        collaborationService.Track("Quote", "42", "vc-001", "alice");
        collaborationService.SetFieldFocus("vc-001", "Quote", "42", "amount", "alice");

        collaborationService.GetFieldPresence("Quote", "42", "amount").Should().HaveCount(1);

        await handler.OnConnectionDownAsync(CreateCircuit(), CancellationToken.None);

        collaborationService.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
    }

    [Fact]
    public async Task OnConnectionDown_Should_DoNothing_When_NoRegisteredCircuits()
    {
        var collaborationService = new CollaborationService();
        var registry = new CircuitPresenceRegistry();
        var handler = new CircuitPresenceHandler(
            collaborationService, registry, NullLogger<CircuitPresenceHandler>.Instance);

        var act = async () => await handler.OnConnectionDownAsync(CreateCircuit(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static Circuit CreateCircuit()
    {
        // Circuit has no public constructor; use reflection to create a test instance.
        return (Circuit)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Circuit));
    }
}
