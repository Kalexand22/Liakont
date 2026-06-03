namespace Stratum.Common.Infrastructure.Tests.Unit.UiRules;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.UiRules;
using Stratum.Common.Infrastructure.UiRules;
using Xunit;

public sealed class UiRuleEngineTests
{
    [Fact]
    public void Evaluate_EmptyRules_Returns_Empty_AttributeSet()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Draft", Discount = 10m };

        var result = engine.Evaluate(dto, []);

        result.Count.Should().Be(0);
    }

    [Fact]
    public void Evaluate_HiddenWhen_True_Sets_Hidden()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
        };

        var result = engine.Evaluate(dto, rules);

        result.Should().ContainKey("Discount");
        result["Discount"].Hidden.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_HiddenWhen_False_Sets_Hidden_False()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Draft", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
        };

        var result = engine.Evaluate(dto, rules);

        result.Should().ContainKey("Discount");
        result["Discount"].Hidden.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ReadOnlyWhen_True_Sets_ReadOnly()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .ReadOnlyWhen(x => x.Status == "Confirmed"),
        };

        var result = engine.Evaluate(dto, rules);

        result["Discount"].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RequiredWhen_True_Sets_Required()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.DeliveryDate!)
                .RequiredWhen(x => x.Status == "Confirmed"),
        };

        var result = engine.Evaluate(dto, rules);

        result["DeliveryDate"].Required.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DomainFilter_Returns_Filter_String()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Active", Discount = 0m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.ClientId!)
                .WithDomainFilter(x => $"status == '{x.Status}'"),
        };

        var result = engine.Evaluate(dto, rules);

        result["ClientId"].DomainFilter.Should().Be("status == 'Active'");
    }

    [Fact]
    public void Evaluate_Multiple_Rules_Same_Field_Merges_With_OR()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
            Rule.For<InvoiceDto>(x => x.Discount)
                .ReadOnlyWhen(x => x.Status == "Confirmed"),
        };

        var result = engine.Evaluate(dto, rules);

        result["Discount"].Hidden.Should().BeTrue();
        result["Discount"].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Multiple_Fields_Returns_All()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
            Rule.For<InvoiceDto>(x => x.DeliveryDate!)
                .RequiredWhen(x => x.Status == "Confirmed"),
        };

        var result = engine.Evaluate(dto, rules);

        result.Count.Should().Be(2);
        result.Should().ContainKey("Discount");
        result.Should().ContainKey("DeliveryDate");
    }

    [Fact]
    public void Evaluate_Null_Predicates_Default_To_False()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Draft", Discount = 0m };

        // Rule with only field expression, no predicates — should produce all-false attributes.
        var rules = new UiRule<InvoiceDto>[]
        {
            new() { FieldExpression = x => x.Discount },
        };

        var result = engine.Evaluate(dto, rules);

        result["Discount"].Hidden.Should().BeFalse();
        result["Discount"].ReadOnly.Should().BeFalse();
        result["Discount"].Required.Should().BeFalse();
        result["Discount"].DomainFilter.Should().BeNull();
    }

    [Fact]
    public void Evaluate_Caches_Compiled_Delegates_Same_Expression_Instance()
    {
        var engine = CreateEngine();
        var dto1 = new InvoiceDto { Status = "Draft", Discount = 0m };
        var dto2 = new InvoiceDto { Status = "Confirmed", Discount = 10m };

        // Same rule instance evaluated twice — second call should use cached delegate.
        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
        };

        // Get the static cache via reflection to verify delegate reuse.
        var cacheField = typeof(Stratum.Common.Infrastructure.UiRules.UiRuleEngine)
            .GetField("CompiledCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cache = (ConcurrentDictionary<Expression, Delegate>)cacheField.GetValue(null)!;
        var countBefore = cache.Count;

        engine.Evaluate(dto1, rules);
        var countAfterFirst = cache.Count;

        engine.Evaluate(dto2, rules);
        var countAfterSecond = cache.Count;

        // First call adds entries; second call with same expression instances adds none.
        countAfterFirst.Should().BeGreaterThan(countBefore);
        countAfterSecond.Should().Be(countAfterFirst);
    }

    [Fact]
    public void Evaluate_Graceful_On_Bad_FieldExpression_Skips_Rule()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Draft", Discount = 0m };

        // Create a rule with an expression the engine can't extract a field name from.
        var badExpression = (System.Linq.Expressions.Expression<Func<InvoiceDto, object>>)(x => x.Status + x.Discount);

        var rules = new UiRule<InvoiceDto>[]
        {
            new()
            {
                FieldExpression = badExpression,
                HiddenWhen = x => true,
            },
            Rule.For<InvoiceDto>(x => x.Discount)
                .RequiredWhen(x => x.Status == "Draft"),
        };

        // Should not throw — bad rule is skipped, good rule is evaluated.
        var result = engine.Evaluate(dto, rules);

        result.Count.Should().Be(1);
        result["Discount"].Required.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_All_Attributes_Combined_On_Single_Rule()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Confirmed", Discount = 5m };

        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft")
                .ReadOnlyWhen(x => x.Status == "Confirmed")
                .RequiredWhen(x => x.Discount > 0),
        };

        var result = engine.Evaluate(dto, rules);

        result["Discount"].Hidden.Should().BeTrue();
        result["Discount"].ReadOnly.Should().BeTrue();
        result["Discount"].Required.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ValueType_Field_Extracts_Name_Through_Boxing()
    {
        var engine = CreateEngine();
        var dto = new InvoiceDto { Status = "Draft", Discount = 42m };

        // Discount is decimal (value type) → expression will have Convert node.
        var rules = new UiRule<InvoiceDto>[]
        {
            Rule.For<InvoiceDto>(x => x.Discount)
                .ReadOnlyWhen(x => x.Discount > 100),
        };

        var result = engine.Evaluate(dto, rules);

        result.Should().ContainKey("Discount");
        result["Discount"].ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void AddStratumUiRuleEngine_Registers_Singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumUiRuleEngine();

        using var sp = services.BuildServiceProvider();

        var engine1 = sp.GetRequiredService<IUiRuleEngine>();
        var engine2 = sp.GetRequiredService<IUiRuleEngine>();

        engine1.Should().NotBeNull();
        engine1.Should().BeOfType<Stratum.Common.Infrastructure.UiRules.UiRuleEngine>();
        engine1.Should().BeSameAs(engine2, "because UiRuleEngine is registered as singleton");
    }

    private static IUiRuleEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumUiRuleEngine();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IUiRuleEngine>();
    }

    public sealed record InvoiceDto
    {
        public string Status { get; init; } = string.Empty;

        public decimal Discount { get; init; }

        public string? DeliveryDate { get; init; }

        public string? ClientId { get; init; }
    }
}
