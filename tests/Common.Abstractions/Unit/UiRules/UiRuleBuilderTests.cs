namespace Stratum.Common.Abstractions.Tests.Unit.UiRules;

using System.Linq.Expressions;
using FluentAssertions;
using Stratum.Common.Abstractions.UiRules;
using Xunit;

public sealed class UiRuleBuilderTests
{
    [Fact]
    public void Build_WithAllPredicates_ShouldPopulateAllFields()
    {
        var rule = Rule.For<SampleDto>(x => x.Discount)
            .HiddenWhen(x => x.Status != "Draft")
            .ReadOnlyWhen(x => x.Status == "Confirmed")
            .RequiredWhen(x => x.Status == "Validated")
            .WithDomainFilter(x => $"status == '{x.Status}'")
            .Build();

        rule.FieldExpression.Should().NotBeNull();
        rule.HiddenWhen.Should().NotBeNull();
        rule.ReadOnlyWhen.Should().NotBeNull();
        rule.RequiredWhen.Should().NotBeNull();
        rule.DomainFilter.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithNoPredicates_ShouldHaveNullConditions()
    {
        var rule = Rule.For<SampleDto>(x => x.Discount).Build();

        rule.FieldExpression.Should().NotBeNull();
        rule.HiddenWhen.Should().BeNull();
        rule.ReadOnlyWhen.Should().BeNull();
        rule.RequiredWhen.Should().BeNull();
        rule.DomainFilter.Should().BeNull();
    }

    [Fact]
    public void ImplicitConversion_ShouldProduceUiRule()
    {
        UiRule<SampleDto> rule = Rule.For<SampleDto>(x => x.Partner)
            .HiddenWhen(x => x.Status == "Cancelled");

        rule.Should().NotBeNull();
        rule.HiddenWhen.Should().NotBeNull();
    }

    [Fact]
    public void For_NullExpression_ShouldThrow()
    {
        var act = () => Rule.For<SampleDto>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HiddenWhen_NullPredicate_ShouldThrow()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount);

        var act = () => builder.HiddenWhen(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadOnlyWhen_NullPredicate_ShouldThrow()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount);

        var act = () => builder.ReadOnlyWhen(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RequiredWhen_NullPredicate_ShouldThrow()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount);

        var act = () => builder.RequiredWhen(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithDomainFilter_NullExpression_ShouldThrow()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount);

        var act = () => builder.WithDomainFilter(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HiddenWhen_CalledTwice_ShouldThrowInvalidOperation()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount)
            .HiddenWhen(x => x.Status == "Draft");

        var act = () => builder.HiddenWhen(x => x.Status == "Cancelled");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HiddenWhen*already*");
    }

    [Fact]
    public void ReadOnlyWhen_CalledTwice_ShouldThrowInvalidOperation()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount)
            .ReadOnlyWhen(x => x.Status == "Draft");

        var act = () => builder.ReadOnlyWhen(x => x.Status == "Cancelled");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ReadOnlyWhen*already*");
    }

    [Fact]
    public void RequiredWhen_CalledTwice_ShouldThrowInvalidOperation()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount)
            .RequiredWhen(x => x.Status == "Draft");

        var act = () => builder.RequiredWhen(x => x.Status == "Cancelled");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequiredWhen*already*");
    }

    [Fact]
    public void WithDomainFilter_CalledTwice_ShouldThrowInvalidOperation()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount)
            .WithDomainFilter(x => "filter1");

        var act = () => builder.WithDomainFilter(x => "filter2");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DomainFilter*already*");
    }

    [Fact]
    public void FluentChaining_ShouldReturnSameBuilder()
    {
        var builder = Rule.For<SampleDto>(x => x.Discount);

        var result = builder
            .HiddenWhen(x => x.Status == "A")
            .ReadOnlyWhen(x => x.Status == "B")
            .RequiredWhen(x => x.Status == "C");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Build_Expressions_ShouldBeCompilableAndExecutable()
    {
        var rule = Rule.For<SampleDto>(x => x.Discount)
            .HiddenWhen(x => x.Status != "Draft")
            .Build();

        var compiled = rule.HiddenWhen!.Compile();

        compiled(new SampleDto("Draft", 10m, "Acme")).Should().BeFalse();
        compiled(new SampleDto("Confirmed", 10m, "Acme")).Should().BeTrue();
    }

    private sealed record SampleDto(string Status, decimal Discount, string Partner);
}
