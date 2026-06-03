namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class CollaborationServiceFieldLockTests
{
    [Fact]
    public void IsFieldLocked_Should_ReturnNull_When_NobodyHasFocus()
    {
        var sut = CreateService();

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnNull_When_OnlySelfHasFocus()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnLockerName_When_AnotherCircuitHasFocus()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().Be("bob");
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnNull_After_OtherCircuitClearsFocus()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");
        sut.ClearFieldFocus("circuit-2", "amount");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnNull_When_FocusExpiredByTtl()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = CreateService(timeProvider);

        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance time past TTL (60s)
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnLockerName_When_FocusWithinTtl()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = CreateService(timeProvider);

        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance time but stay within TTL
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().Be("bob");
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnNull_When_FocusExactlyAtTtlBoundary()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = CreateService(timeProvider);

        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance exactly to TTL boundary — entry is at cutoff, cutoff = now - TTL,
        // entry.FocusedAt >= cutoff should still be true (equal)
        timeProvider.Advance(TimeSpan.FromSeconds(60));

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().Be("bob");
    }

    [Fact]
    public void IsFieldLocked_Should_IsolateFields()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-2", "Quote", "42", "name", "bob");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_IsolateEntities()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-2", "Quote", "99", "amount", "bob");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().BeNull();
    }

    [Fact]
    public void IsFieldLocked_Should_ReturnFirstLockerName_When_MultipleOtherCircuitsHaveFocus()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");
        sut.SetFieldFocus("circuit-3", "Quote", "42", "amount", "charlie");

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().NotBeNull();
        result.Should().BeOneOf("bob", "charlie");
    }

    [Fact]
    public void IsFieldLocked_Should_RenewLock_When_SetFieldFocusCalledAgain()
    {
        var timeProvider = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var sut = CreateService(timeProvider);

        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance 50s (within TTL)
        timeProvider.Advance(TimeSpan.FromSeconds(50));

        // Bob renews focus
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        // Advance another 50s (total 100s from original, but only 50s from renewal)
        timeProvider.Advance(TimeSpan.FromSeconds(50));

        var result = sut.IsFieldLocked("Quote", "42", "amount", "circuit-1");

        result.Should().Be("bob");
    }

    [Fact]
    public void FieldLockTtl_Should_Be60Seconds()
    {
        var sut = CreateService();

        sut.FieldLockTtl.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsFieldLocked_Should_Throw_When_EntityTypeIsInvalid(string? entityType)
    {
        var sut = CreateService();

        var act = () => sut.IsFieldLocked(entityType!, "42", "amount", "circuit-1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsFieldLocked_Should_Throw_When_EntityIdIsInvalid(string? entityId)
    {
        var sut = CreateService();

        var act = () => sut.IsFieldLocked("Quote", entityId!, "amount", "circuit-1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsFieldLocked_Should_Throw_When_FieldNameIsInvalid(string? fieldName)
    {
        var sut = CreateService();

        var act = () => sut.IsFieldLocked("Quote", "42", fieldName!, "circuit-1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsFieldLocked_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = CreateService();

        var act = () => sut.IsFieldLocked("Quote", "42", "amount", circuitId!);

        act.Should().Throw<ArgumentException>();
    }

    private static CollaborationService CreateService() => new();

    private static CollaborationService CreateService(TimeProvider timeProvider) => new(timeProvider);

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
