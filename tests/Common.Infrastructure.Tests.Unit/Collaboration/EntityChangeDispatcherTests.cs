namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class EntityChangeDispatcherTests
{
    private readonly EntityChangeDispatcher _sut = new(NullLogger<EntityChangeDispatcher>.Instance);

    private static EntityChangedEvent MakeEvent(string changedBy = "alice") =>
        new("Quote", "42", changedBy, DateTimeOffset.UtcNow);

    [Fact]
    public async Task NotifyEntityChangedAsync_InvokesSubscribedCallback()
    {
        EntityChangedEvent? received = null;
        _sut.Subscribe("c1", evt => received = evt);

        var sent = MakeEvent();
        var circuits = new List<PresenceEntry> { new("c1", "alice") };

        await _sut.NotifyEntityChangedAsync(sent, circuits);

        received.Should().BeSameAs(sent);
    }

    [Fact]
    public async Task NotifyEntityChangedAsync_IgnoresUnsubscribedCircuit()
    {
        EntityChangedEvent? received = null;
        _sut.Subscribe("c1", evt => received = evt);
        _sut.Unsubscribe("c1");

        await _sut.NotifyEntityChangedAsync(MakeEvent(), new List<PresenceEntry> { new("c1", "alice") });

        received.Should().BeNull();
    }

    [Fact]
    public async Task NotifyEntityChangedAsync_OnlyNotifiesTargetedCircuits()
    {
        EntityChangedEvent? received1 = null;
        EntityChangedEvent? received2 = null;

        _sut.Subscribe("c1", evt => received1 = evt);
        _sut.Subscribe("c2", evt => received2 = evt);

        var sent = MakeEvent();
        await _sut.NotifyEntityChangedAsync(sent, new List<PresenceEntry> { new("c2", "bob") });

        received1.Should().BeNull();
        received2.Should().BeSameAs(sent);
    }

    [Fact]
    public async Task NotifyEntityChangedAsync_ContinuesAfterCallbackException()
    {
        EntityChangedEvent? received = null;

        _sut.Subscribe("c1", _ => throw new InvalidOperationException("boom"));
        _sut.Subscribe("c2", evt => received = evt);

        var sent = MakeEvent();
        var circuits = new List<PresenceEntry>
        {
            new("c1", "alice"),
            new("c2", "bob"),
        };

        await _sut.NotifyEntityChangedAsync(sent, circuits);

        received.Should().BeSameAs(sent, "c2 should still receive the event even though c1 threw");
    }

    [Fact]
    public void Subscribe_ReplacesExistingCallback()
    {
        int callCount1 = 0, callCount2 = 0;

        _sut.Subscribe("c1", _ => callCount1++);
        _sut.Subscribe("c1", _ => callCount2++);

        _sut.NotifyEntityChangedAsync(MakeEvent(), new List<PresenceEntry> { new("c1", "alice") });

        callCount1.Should().Be(0, "first callback should have been replaced");
        callCount2.Should().Be(1);
    }

    [Fact]
    public async Task NotifyEntityChangedAsync_NoSubscribers_DoesNotThrow()
    {
        var act = () => _sut.NotifyEntityChangedAsync(
            MakeEvent(),
            new List<PresenceEntry> { new("missing", "nobody") });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Unsubscribe_NonExistentCircuit_DoesNotThrow()
    {
        var act = () => _sut.Unsubscribe("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void Subscribe_NullCircuitId_Throws()
    {
        var act = () => _sut.Subscribe(null!, _ => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Subscribe_NullCallback_Throws()
    {
        var act = () => _sut.Subscribe("c1", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
