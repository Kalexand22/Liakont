namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class FormRegistryTests
{
    // ── Resolve ─────────────────────────────────────────────────────────
    [Fact]
    public void ResolveShouldReturnDefaultFormWhenNoContextKey()
    {
        var registry = BuildRegistry(new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null));

        var result = registry.Resolve<SampleOrder>();

        result.Should().Be<OrderDefaultForm>();
    }

    [Fact]
    public void ResolveShouldReturnContextOverrideWhenContextKeyMatches()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null),
            new FormRegistration(typeof(SampleOrder), typeof(OrderQuickForm), "quick-edit"));

        var result = registry.Resolve<SampleOrder>("quick-edit");

        result.Should().Be<OrderQuickForm>();
    }

    [Fact]
    public void ResolveShouldFallBackToDefaultWhenContextKeyNotFound()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null));

        var result = registry.Resolve<SampleOrder>("unknown-context");

        result.Should().Be<OrderDefaultForm>();
    }

    [Fact]
    public void ResolveShouldThrowWhenNoRegistration()
    {
        var registry = BuildRegistry();

        var act = () => registry.Resolve<SampleOrder>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SampleOrder*");
    }

    // ── TryResolve ──────────────────────────────────────────────────────
    [Fact]
    public void TryResolveShouldReturnFalseWhenNoRegistration()
    {
        var registry = BuildRegistry();

        registry.TryResolve<SampleOrder>(null, out var type).Should().BeFalse();
        type.Should().BeNull();
    }

    [Fact]
    public void TryResolveShouldReturnTrueWithContextOverride()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null),
            new FormRegistration(typeof(SampleOrder), typeof(OrderQuickForm), "quick-edit"));

        registry.TryResolve<SampleOrder>("quick-edit", out var type).Should().BeTrue();
        type.Should().Be<OrderQuickForm>();
    }

    // ── Duplicate detection ─────────────────────────────────────────────
    [Fact]
    public void ConstructorShouldUseLastWinsForDuplicateRegistration()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null),
            new FormRegistration(typeof(SampleOrder), typeof(OrderQuickForm), null));

        registry.Resolve<SampleOrder>().Should().Be<OrderQuickForm>();
    }

    // ── DI extension wiring ─────────────────────────────────────────────
    [Fact]
    public void RegisterFormExtensionShouldWireThroughDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormRegistry, FormRegistry>();
        services.RegisterForm<SampleOrder, OrderDefaultForm>();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IFormRegistry>();

        registry.Resolve<SampleOrder>().Should().Be<OrderDefaultForm>();
    }

    // ── Register (runtime) ──────────────────────────────────────────────
    [Fact]
    public void RegisterShouldAddFormAtRuntime()
    {
        var registry = BuildRegistry();

        registry.Register<SampleProduct, ProductDefaultForm>();

        registry.Resolve<SampleProduct>().Should().Be<ProductDefaultForm>();
    }

    [Fact]
    public void RegisterShouldOverrideExistingRegistration()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null));

        registry.Register<SampleOrder, OrderQuickForm>();

        registry.Resolve<SampleOrder>().Should().Be<OrderQuickForm>();
    }

    // ── Multiple entities ───────────────────────────────────────────────
    [Fact]
    public void MultipleEntitiesShouldResolveIndependently()
    {
        var registry = BuildRegistry(
            new FormRegistration(typeof(SampleOrder), typeof(OrderDefaultForm), null),
            new FormRegistration(typeof(SampleProduct), typeof(ProductDefaultForm), null));

        registry.Resolve<SampleOrder>().Should().Be<OrderDefaultForm>();
        registry.Resolve<SampleProduct>().Should().Be<ProductDefaultForm>();
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private static FormRegistry BuildRegistry(params FormRegistration[] registrations) =>
        new(registrations);

    // ── Test types ──────────────────────────────────────────────────────
    internal sealed record SampleOrder(Guid Id, string Status);

    internal sealed record SampleProduct(Guid Id, string Name);

    internal sealed class OrderDefaultForm : ComponentBase
    {
    }

    internal sealed class OrderQuickForm : ComponentBase
    {
    }

    internal sealed class ProductDefaultForm : ComponentBase
    {
    }
}
