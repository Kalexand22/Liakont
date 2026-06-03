namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Domain.Services;
using Xunit;

public sealed class ConditionParserTests
{
    // ── INV-IDENT-016: Validate ────────────────────────────────────────────

    [Theory]
    [InlineData("record.company_id == actor.company_id")]
    [InlineData("record.owner_id == actor.user_id")]
    [InlineData("record.status != \"Archived\"")]
    [InlineData("actor.language == \"fr\"")]
    public void ValidateShouldAcceptValidConditions(string condition)
    {
        var result = ConditionParser.Validate(condition);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("record.field")]
    [InlineData("record.field > actor.field")]
    [InlineData("record.field == ")]
    [InlineData("== actor.field")]
    [InlineData("something.field == actor.company_id")]
    public void ValidateShouldRejectInvalidConditions(string condition)
    {
        var result = ConditionParser.Validate(condition);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateShouldRejectNullCondition()
    {
        var result = ConditionParser.Validate(null!);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateShouldRejectUnknownActorField()
    {
        var result = ConditionParser.Validate("actor.unknown_field == \"test\"");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown actor field");
    }

    // ── INV-IDENT-017: Evaluate ────────────────────────────────────────────

    [Fact]
    public void EvaluateShouldReturnTrueWhenEqualityMatches()
    {
        var companyId = Guid.NewGuid();
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = companyId };
        var resource = new Dictionary<string, object?> { ["company_id"] = companyId.ToString() };

        var result = ConditionParser.Evaluate("record.company_id == actor.company_id", actor, resource);

        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateShouldReturnFalseWhenEqualityDoesNotMatch()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        var resource = new Dictionary<string, object?> { ["company_id"] = Guid.NewGuid().ToString() };

        var result = ConditionParser.Evaluate("record.company_id == actor.company_id", actor, resource);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateShouldReturnTrueForInequalityWhenDifferent()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        var resource = new Dictionary<string, object?> { ["status"] = "Active" };

        var result = ConditionParser.Evaluate("record.status != \"Archived\"", actor, resource);

        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateShouldReturnFalseForInequalityWhenEqual()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        var resource = new Dictionary<string, object?> { ["status"] = "Archived" };

        var result = ConditionParser.Evaluate("record.status != \"Archived\"", actor, resource);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateShouldReturnFalseForInvalidCondition()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };

        var result = ConditionParser.Evaluate("invalid expression", actor, null);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateShouldReturnFalseWhenResourceContextIsNull()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };

        var result = ConditionParser.Evaluate("record.company_id == actor.company_id", actor, null);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateShouldReturnFalseWhenRecordFieldMissing()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        var resource = new Dictionary<string, object?>();

        var result = ConditionParser.Evaluate("record.company_id == actor.company_id", actor, resource);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateShouldHandleActorUserIdComparison()
    {
        var userId = Guid.NewGuid();
        var actor = new FakeActorContext { UserId = userId, CompanyId = Guid.NewGuid() };
        var resource = new Dictionary<string, object?> { ["owner_id"] = userId.ToString() };

        var result = ConditionParser.Evaluate("record.owner_id == actor.user_id", actor, resource);

        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateShouldHandleStringLiteralComparison()
    {
        var actor = new FakeActorContext { UserId = Guid.NewGuid(), Language = "fr" };

        var result = ConditionParser.Evaluate("actor.language == \"fr\"", actor, null);

        result.Should().BeTrue();
    }

    // ── Grant entity ────────────────────────────────────────────────────────

    [Fact]
    public void GrantCreateShouldAcceptNullCondition()
    {
        var grant = Domain.Entities.Grant.Create(Guid.NewGuid(), "test.read", "test");

        grant.Condition.Should().BeNull();
    }

    [Fact]
    public void GrantCreateShouldAcceptValidCondition()
    {
        var grant = Domain.Entities.Grant.Create(Guid.NewGuid(), "test.read", "test", "record.company_id == actor.company_id");

        grant.Condition.Should().Be("record.company_id == actor.company_id");
    }

    [Fact]
    public void GrantCreateShouldRejectInvalidCondition()
    {
        var act = () => Domain.Entities.Grant.Create(Guid.NewGuid(), "test.read", "test", "invalid");

        act.Should().Throw<ArgumentException>().WithMessage("*INV-IDENT-016*");
    }

    private sealed class FakeActorContext : IActorContext
    {
        public Guid UserId { get; init; }

        public Guid CorrelationId { get; init; }

        public bool IsAuthenticated { get; init; } = true;

        public string? DisplayName { get; init; }

        public string? Email { get; init; }

        public Guid? CompanyId { get; init; }

        public string? Timezone { get; init; }

        public string? Language { get; init; }

        public string? TenantId { get; init; }
    }
}
