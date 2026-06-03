namespace Stratum.Common.Infrastructure.Tests.Unit.Collaboration;

using FluentAssertions;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Infrastructure.Collaboration;
using Xunit;

public sealed class CollaborationServiceFieldFocusTests
{
    [Fact]
    public void GetFieldPresence_Should_ReturnEmpty_When_NothingFocused()
    {
        var sut = CreateService();

        var result = sut.GetFieldPresence("Quote", "42", "amount");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SetFieldFocus_Should_RegisterFocus()
    {
        var sut = CreateService();

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().ContainSingle()
            .Which.Should().Match<FieldFocusEntry>(e =>
                e.CircuitId == "circuit-1" && e.User == "alice");
    }

    [Fact]
    public void SetFieldFocus_Should_SupportMultipleCircuitsOnSameField()
    {
        var sut = CreateService();

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().HaveCount(2);
        result.Should().Contain(e => e.CircuitId == "circuit-1" && e.User == "alice");
        result.Should().Contain(e => e.CircuitId == "circuit-2" && e.User == "bob");
    }

    [Fact]
    public void SetFieldFocus_Should_ReplacePreviousFocus_When_SameCircuitFocusesAgain()
    {
        var sut = CreateService();

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice-renamed");

        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().ContainSingle()
            .Which.User.Should().Be("alice-renamed");
    }

    [Fact]
    public void SetFieldFocus_Should_IsolateFields()
    {
        var sut = CreateService();

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-2", "Quote", "42", "name", "bob");

        sut.GetFieldPresence("Quote", "42", "amount").Should().ContainSingle();
        sut.GetFieldPresence("Quote", "42", "name").Should().ContainSingle();
        sut.GetFieldPresence("Quote", "42", "description").Should().BeEmpty();
    }

    [Fact]
    public void SetFieldFocus_Should_IsolateEntities()
    {
        var sut = CreateService();

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-2", "Party", "99", "amount", "bob");

        sut.GetFieldPresence("Quote", "42", "amount").Should().ContainSingle();
        sut.GetFieldPresence("Party", "99", "amount").Should().ContainSingle();
        sut.GetFieldPresence("Quote", "99", "amount").Should().BeEmpty();
    }

    [Fact]
    public void SetFieldFocus_Should_SetFocusedAtTimestamp()
    {
        var sut = CreateService();
        var before = DateTimeOffset.UtcNow;

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        var after = DateTimeOffset.UtcNow;
        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().ContainSingle()
            .Which.FocusedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void SetFieldFocus_Should_RaiseOnFieldPresenceChanged()
    {
        var sut = CreateService();
        var raised = false;
        sut.OnFieldPresenceChanged += () => raised = true;

        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        raised.Should().BeTrue();
    }

    [Fact]
    public void ClearFieldFocus_Should_RemoveSpecificField_When_FieldNameProvided()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "name", "alice");

        sut.ClearFieldFocus("circuit-1", "amount");

        sut.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
        sut.GetFieldPresence("Quote", "42", "name").Should().ContainSingle();
    }

    [Fact]
    public void ClearFieldFocus_Should_RemoveAllFields_When_NoFieldNameProvided()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "name", "alice");

        sut.ClearFieldFocus("circuit-1");

        sut.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
        sut.GetFieldPresence("Quote", "42", "name").Should().BeEmpty();
    }

    [Fact]
    public void ClearFieldFocus_Should_NotAffectOtherCircuits()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        sut.ClearFieldFocus("circuit-1", "amount");

        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().ContainSingle()
            .Which.Should().Match<FieldFocusEntry>(e =>
                e.CircuitId == "circuit-2" && e.User == "bob");
    }

    [Fact]
    public void ClearFieldFocus_Should_BeIdempotent_When_CircuitNotFocused()
    {
        var sut = CreateService();

        var act = () => sut.ClearFieldFocus("nonexistent", "amount");

        act.Should().NotThrow();
    }

    [Fact]
    public void ClearFieldFocus_Should_RaiseOnFieldPresenceChanged_When_FocusWasActive()
    {
        var sut = CreateService();
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        var raised = false;
        sut.OnFieldPresenceChanged += () => raised = true;

        sut.ClearFieldFocus("circuit-1", "amount");

        raised.Should().BeTrue();
    }

    [Fact]
    public void ClearFieldFocus_Should_NotRaiseOnFieldPresenceChanged_When_NothingWasActive()
    {
        var sut = CreateService();
        var raised = false;
        sut.OnFieldPresenceChanged += () => raised = true;

        sut.ClearFieldFocus("nonexistent");

        raised.Should().BeFalse();
    }

    [Fact]
    public void Untrack_Should_ClearFieldFocus_ForThatCircuit()
    {
        var sut = CreateService();
        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");

        sut.Untrack("circuit-1");

        sut.GetFieldPresence("Quote", "42", "amount").Should().BeEmpty();
    }

    [Fact]
    public void Untrack_Should_NotAffectOtherCircuitsFieldFocus()
    {
        var sut = CreateService();
        sut.Track("Quote", "42", "circuit-1", "alice");
        sut.Track("Quote", "42", "circuit-2", "bob");
        sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", "alice");
        sut.SetFieldFocus("circuit-2", "Quote", "42", "amount", "bob");

        sut.Untrack("circuit-1");

        var result = sut.GetFieldPresence("Quote", "42", "amount");
        result.Should().ContainSingle()
            .Which.CircuitId.Should().Be("circuit-2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SetFieldFocus_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = CreateService();

        var act = () => sut.SetFieldFocus(circuitId!, "Quote", "42", "amount", "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SetFieldFocus_Should_Throw_When_EntityTypeIsInvalid(string? entityType)
    {
        var sut = CreateService();

        var act = () => sut.SetFieldFocus("circuit-1", entityType!, "42", "amount", "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SetFieldFocus_Should_Throw_When_EntityIdIsInvalid(string? entityId)
    {
        var sut = CreateService();

        var act = () => sut.SetFieldFocus("circuit-1", "Quote", entityId!, "amount", "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SetFieldFocus_Should_Throw_When_FieldNameIsInvalid(string? fieldName)
    {
        var sut = CreateService();

        var act = () => sut.SetFieldFocus("circuit-1", "Quote", "42", fieldName!, "alice");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SetFieldFocus_Should_Throw_When_UserIsInvalid(string? user)
    {
        var sut = CreateService();

        var act = () => sut.SetFieldFocus("circuit-1", "Quote", "42", "amount", user!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ClearFieldFocus_Should_Throw_When_CircuitIdIsInvalid(string? circuitId)
    {
        var sut = CreateService();

        var act = () => sut.ClearFieldFocus(circuitId!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetFieldPresence_Should_Throw_When_EntityTypeIsInvalid(string? entityType)
    {
        var sut = CreateService();

        var act = () => sut.GetFieldPresence(entityType!, "42", "amount");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetFieldPresence_Should_Throw_When_EntityIdIsInvalid(string? entityId)
    {
        var sut = CreateService();

        var act = () => sut.GetFieldPresence("Quote", entityId!, "amount");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetFieldPresence_Should_Throw_When_FieldNameIsInvalid(string? fieldName)
    {
        var sut = CreateService();

        var act = () => sut.GetFieldPresence("Quote", "42", fieldName!);

        act.Should().Throw<ArgumentException>();
    }

    private static CollaborationService CreateService() => new();
}
