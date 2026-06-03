namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class CollaborationServiceTests
{
    [Fact]
    public void GetPresence_Should_ReturnEmpty_When_NothingTracked()
    {
        var sut = CreateService();

        var result = sut.GetPresence("Quote", "42");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Track_Should_RegisterPresence()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");

        var result = sut.GetPresence("Quote", "42");
        result.Should().ContainSingle()
            .Which.Should().Be(new PresenceEntry("circuit-1", "alice"));
    }

    [Fact]
    public void Track_Should_SupportMultipleCircuitsOnSameEntity()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Quote", "42", "circuit-2", "bob");

        var result = sut.GetPresence("Quote", "42");
        result.Should().HaveCount(2);
        result.Should().Contain(new PresenceEntry("circuit-1", "alice"));
        result.Should().Contain(new PresenceEntry("circuit-2", "bob"));
    }

    [Fact]
    public void Track_Should_UpdateUser_When_SameCircuitTracksAgain()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Quote", "42", "circuit-1", "alice-renamed");

        var result = sut.GetPresence("Quote", "42");
        result.Should().ContainSingle()
            .Which.User.Should().Be("alice-renamed");
    }

    [Fact]
    public void Track_Should_IsolateEntities()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Party", "99", "circuit-2", "bob");

        sut.GetPresence("Quote", "42").Should().ContainSingle();
        sut.GetPresence("Party", "99").Should().ContainSingle();
        sut.GetPresence("Quote", "99").Should().BeEmpty();
    }

    [Fact]
    public void Untrack_Should_RemoveAllPresenceForCircuit()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Party", "99", "circuit-1", "alice");

        sut.Untrack("circuit-1");

        sut.GetPresence("Quote", "42").Should().BeEmpty();
        sut.GetPresence("Party", "99").Should().BeEmpty();
    }

    [Fact]
    public void Untrack_Should_NotAffectOtherCircuits()
    {
        var sut = CreateService();

        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Quote", "42", "circuit-2", "bob");

        sut.Untrack("circuit-1");

        var result = sut.GetPresence("Quote", "42");
        result.Should().ContainSingle()
            .Which.Should().Be(new PresenceEntry("circuit-2", "bob"));
    }

    [Fact]
    public void Untrack_Should_BeIdempotent_When_CircuitNotTracked()
    {
        var sut = CreateService();

        var act = () => sut.Untrack("nonexistent");

        act.Should().NotThrow();
    }

    [Fact]
    public void Track_Should_RaiseOnPresenceChanged()
    {
        var sut = CreateService();
        var raised = false;
        sut.OnPresenceChanged += () => raised = true;

        sut.Track("Quote", "42", "circuit-1", "alice");

        raised.Should().BeTrue();
    }

    [Fact]
    public void Untrack_Should_RaiseOnPresenceChanged_When_CircuitWasTracked()
    {
        var sut = CreateService();
        sut.Track("Quote", "42", "circuit-1", "alice");

        var raised = false;
        sut.OnPresenceChanged += () => raised = true;

        sut.Untrack("circuit-1");

        raised.Should().BeTrue();
    }

    [Fact]
    public void Untrack_Should_NotRaiseOnPresenceChanged_When_CircuitWasNotTracked()
    {
        var sut = CreateService();
        var raised = false;
        sut.OnPresenceChanged += () => raised = true;

        sut.Untrack("nonexistent");

        raised.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Track_Should_Throw_When_EntityTypeIsInvalid(string? entityType)
    {
        var sut = CreateService();

        var act = () => sut.Track(entityType!, "42", "circuit-1", "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Track_Should_Throw_When_EntityIdIsInvalid(string? entityId)
    {
        var sut = CreateService();

        var act = () => sut.Track("Quote", entityId!, "circuit-1", "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Track_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = CreateService();

        var act = () => sut.Track("Quote", "42", circuitId!, "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Track_Should_Throw_When_UserIsInvalid(string? user)
    {
        var sut = CreateService();

        var act = () => sut.Track("Quote", "42", "circuit-1", user!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Untrack_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = CreateService();

        var act = () => sut.Untrack(circuitId!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetPresence_Should_Throw_When_EntityTypeIsInvalid(string? entityType)
    {
        var sut = CreateService();

        var act = () => sut.GetPresence(entityType!, "42");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetPresence_Should_Throw_When_EntityIdIsInvalid(string? entityId)
    {
        var sut = CreateService();

        var act = () => sut.GetPresence("Quote", entityId!);

        act.Should().Throw<ArgumentException>();
    }

    private static CollaborationService CreateService() => new();
}
