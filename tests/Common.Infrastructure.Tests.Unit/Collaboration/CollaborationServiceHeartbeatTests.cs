namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class CollaborationServiceHeartbeatTests
{
    [Fact]
    public void RenewFieldFocus_Should_UpdateTimestamp()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        var originalTimestamp = sut.GetFieldPresence("Quote", "42", "amount")[0].FocusedAt;

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        sut.RenewFieldFocus("circuit-1");

        var renewed = sut.GetFieldPresence("Quote", "42", "amount")[0];
        renewed.FocusedAt.Should().BeAfter(originalTimestamp);
    }

    [Fact]
    public void RenewFieldFocus_Should_ExtendLockBeyondOriginalTtl()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance 50s (within TTL)
        timeProvider.Advance(TimeSpan.FromSeconds(50));

        // Renew
        sut.RenewFieldFocus("circuit-2");

        // Advance another 50s (100s total, but only 50s since renewal)
        timeProvider.Advance(TimeSpan.FromSeconds(50));

        // Lock should still be active
        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");
        result.Should().Be("bob");
    }

    [Fact]
    public void RenewFieldFocus_Should_DoNothing_When_CircuitHasNoFocus()
    {
        var sut = new CollaborationService();

        // Should not throw
        var act = () => sut.RenewFieldFocus("circuit-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void RenewFieldFocus_Should_RenewAllFieldsForCircuit()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "name", "alice");

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        sut.RenewFieldFocus("circuit-1");

        timeProvider.Advance(TimeSpan.FromSeconds(50));

        // Both fields should still be locked (70s total, but only 50s since renewal)
        sut.IsFieldLocked("Quote", "42", "amount", "circuit-2").Should().Be("alice");
        sut.IsFieldLocked("Quote", "42", "name", "circuit-2").Should().Be("alice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RenewFieldFocus_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = new CollaborationService();

        var act = () => sut.RenewFieldFocus(circuitId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PurgeExpiredEntries_Should_RemoveExpiredFocusEntries()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        // Advance past TTL
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        sut.PurgeExpiredEntries();

        sut.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
    }

    [Fact]
    public void PurgeExpiredEntries_Should_KeepActiveFocusEntries()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        // Advance within TTL
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        sut.PurgeExpiredEntries();

        sut.GetFieldPresence("Quote", "42", "amount").Should().HaveCount(1);
    }

    [Fact]
    public void PurgeExpiredEntries_Should_RemoveExpiredAndKeepActive()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        // Advance 50s
        timeProvider.Advance(TimeSpan.FromSeconds(50));

        // Add another focus — this one is fresh
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance 15s more (circuit-1 is at 65s = expired, circuit-2 is at 15s = active)
        timeProvider.Advance(TimeSpan.FromSeconds(15));

        sut.PurgeExpiredEntries();

        var remaining = sut.GetFieldPresence("Quote", "42", "amount");
        remaining.Should().HaveCount(1);
        remaining[0].User.Should().Be("bob");
    }

    [Fact]
    public void PurgeExpiredEntries_Should_RaiseFieldPresenceChanged_When_EntriesPurged()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        timeProvider.Advance(TimeSpan.FromSeconds(61));

        var raised = false;
        sut.OnFieldPresenceChanged += () => raised = true;

        sut.PurgeExpiredEntries();

        raised.Should().BeTrue();
    }

    [Fact]
    public void PurgeExpiredEntries_Should_NotRaiseEvent_When_NothingPurged()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = new CollaborationService(timeProvider);

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        // Within TTL
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        var raised = false;
        sut.OnFieldPresenceChanged += () => raised = true;

        sut.PurgeExpiredEntries();

        raised.Should().BeFalse();
    }

    [Fact]
    public void PurgeExpiredEntries_Should_DoNothing_When_NoFocusEntries()
    {
        var sut = new CollaborationService();

        var act = () => sut.PurgeExpiredEntries();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Minimal settable time provider for testing TTL-based logic.
    /// </summary>
    private sealed class SettableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public SettableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
