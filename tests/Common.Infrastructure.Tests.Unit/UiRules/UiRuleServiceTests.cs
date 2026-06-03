namespace Stratum.Common.Infrastructure.Tests.Unit.UiRules;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.UiRules;
using Stratum.Common.Infrastructure.UiRules;
using Xunit;

public sealed class UiRuleServiceTests
{
    [Fact]
    public void Evaluate_WithNoProviders_Returns_EmptyAttributeSet()
    {
        var service = CreateService<InvoiceDto>();

        var result = service.Evaluate(new InvoiceDto { Status = "Draft" });

        result.Count.Should().Be(0);
    }

    [Fact]
    public void Evaluate_WithSingleProvider_Returns_EvaluatedAttributes()
    {
        var service = CreateService<InvoiceDto>(new InvoiceUiRules());

        var result = service.Evaluate(new InvoiceDto { Status = "Confirmed", Discount = 10m });

        result.Should().ContainKey("Discount");
        result["Discount"].Hidden.Should().BeTrue("Discount is hidden when status is not Draft");
    }

    [Fact]
    public void Evaluate_WithMultipleProviders_Merges_Results()
    {
        var service = CreateService<InvoiceDto>(
            new InvoiceUiRules(),
            new InvoiceDeliveryRules());

        var dto = new InvoiceDto { Status = "Confirmed", Discount = 10m };
        var result = service.Evaluate(dto);

        result.Should().ContainKey("Discount");
        result["Discount"].Hidden.Should().BeTrue();

        result.Should().ContainKey("DeliveryDate");
        result["DeliveryDate"].Required.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenConditionFalse_ReturnsDefault()
    {
        var service = CreateService<InvoiceDto>(new InvoiceUiRules());

        var result = service.Evaluate(new InvoiceDto { Status = "Draft", Discount = 10m });

        // Discount should not be hidden when status is Draft
        if (result.TryGetValue("Discount", out var attrs))
        {
            attrs.Hidden.Should().BeFalse();
        }
    }

    [Fact]
    public void DI_Registration_Resolves_OpenGeneric()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumUiRuleEngine();
        services.AddSingleton<IUiRuleProvider<InvoiceDto>, InvoiceUiRules>();

        var sp = services.BuildServiceProvider();
        var service = sp.GetRequiredService<IUiRuleService<InvoiceDto>>();

        service.Should().NotBeNull();
        service.Should().BeOfType<UiRuleService<InvoiceDto>>();
    }

    [Fact]
    public void DI_Registration_IsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumUiRuleEngine();

        var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var s1 = scope1.ServiceProvider.GetRequiredService<IUiRuleService<InvoiceDto>>();
        var s2 = scope2.ServiceProvider.GetRequiredService<IUiRuleService<InvoiceDto>>();

        s1.Should().NotBeSameAs(s2, "because UiRuleService is scoped — different per scope");
    }

    private static IUiRuleService<T> CreateService<T>(params IUiRuleProvider<T>[] providers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumUiRuleEngine();

        foreach (var provider in providers)
        {
            services.AddSingleton(provider);
        }

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IUiRuleService<T>>();
    }

    private sealed class InvoiceDto
    {
        public string Status { get; set; } = "Draft";

        public decimal Discount { get; set; }

        public DateTime? DeliveryDate { get; set; }
    }

    private sealed class InvoiceUiRules : IUiRuleProvider<InvoiceDto>
    {
        public IEnumerable<UiRule<InvoiceDto>> GetRules() =>
        [
            Rule.For<InvoiceDto>(x => x.Discount)
                .HiddenWhen(x => x.Status != "Draft"),
        ];
    }

    private sealed class InvoiceDeliveryRules : IUiRuleProvider<InvoiceDto>
    {
        public IEnumerable<UiRule<InvoiceDto>> GetRules() =>
        [
            Rule.For<InvoiceDto>(x => x.DeliveryDate!)
                .RequiredWhen(x => x.Status == "Confirmed"),
        ];
    }
}
