namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Display;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class DisplayTemplateRegistryTests
{
    // ── Format ──────────────────────────────────────────────────────────
    [Fact]
    public void FormatShouldUseRegisteredTemplateWhenAvailable()
    {
        var registry = BuildRegistry(services =>
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>());

        var entity = new SampleEntity("Acme Corp", "AC-001");

        registry.Format(entity).Should().Be("AC-001 — Acme Corp");
    }

    [Fact]
    public void FormatShouldFallBackToToStringWhenNoTemplateRegistered()
    {
        var registry = BuildRegistry(_ => { });

        var entity = new SampleEntity("Acme Corp", "AC-001");

        registry.Format(entity).Should().Be(entity.ToString());
    }

    [Fact]
    public void FormatShouldReturnEmptyStringWhenToStringReturnsNull()
    {
        var registry = BuildRegistry(_ => { });

        var entity = new NullToStringEntity();

        registry.Format(entity).Should().BeEmpty();
    }

    // ── HasTemplate ─────────────────────────────────────────────────────
    [Fact]
    public void HasTemplateShouldReturnTrueWhenTemplateRegistered()
    {
        var registry = BuildRegistry(services =>
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>());

        registry.HasTemplate<SampleEntity>().Should().BeTrue();
    }

    [Fact]
    public void HasTemplateShouldReturnFalseWhenNoTemplateRegistered()
    {
        var registry = BuildRegistry(_ => { });

        registry.HasTemplate<SampleEntity>().Should().BeFalse();
    }

    // ── GetTemplate ─────────────────────────────────────────────────────
    [Fact]
    public void GetTemplateShouldReturnInstanceWhenRegistered()
    {
        var registry = BuildRegistry(services =>
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>());

        var template = registry.GetTemplate<SampleEntity>();

        template.Should().NotBeNull();
        template.Should().BeOfType<SampleDisplayTemplate>();
    }

    [Fact]
    public void GetTemplateShouldReturnNullWhenNotRegistered()
    {
        var registry = BuildRegistry(_ => { });

        registry.GetTemplate<SampleEntity>().Should().BeNull();
    }

    // ── FormatObject (runtime type resolution) ─────────────────────────
    [Fact]
    public void FormatObjectShouldUseRegisteredTemplateWhenAvailable()
    {
        var registry = BuildRegistry(services =>
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>());

        object entity = new SampleEntity("Acme Corp", "AC-001");

        registry.FormatObject(entity).Should().Be("AC-001 — Acme Corp");
    }

    [Fact]
    public void FormatObjectShouldFallBackToToStringWhenNoTemplate()
    {
        var registry = BuildRegistry(_ => { });

        object entity = new SampleEntity("Acme Corp", "AC-001");

        registry.FormatObject(entity).Should().Be(entity.ToString());
    }

    [Fact]
    public void FormatObjectShouldResolveCorrectTemplateByRuntimeType()
    {
        var registry = BuildRegistry(services =>
        {
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>();
            services.AddSingleton<IDisplayTemplate<AnotherEntity>, AnotherDisplayTemplate>();
        });

        object sample = new SampleEntity("Acme", "001");
        object another = new AnotherEntity(42);

        registry.FormatObject(sample).Should().Be("001 — Acme");
        registry.FormatObject(another).Should().Be("Entity #42");
    }

    // ── Multiple entity types ───────────────────────────────────────────
    [Fact]
    public void RegistryShouldResolveDistinctTemplatesPerEntityType()
    {
        var registry = BuildRegistry(services =>
        {
            services.AddSingleton<IDisplayTemplate<SampleEntity>, SampleDisplayTemplate>();
            services.AddSingleton<IDisplayTemplate<AnotherEntity>, AnotherDisplayTemplate>();
        });

        var sample = new SampleEntity("Acme", "001");
        var another = new AnotherEntity(42);

        registry.Format(sample).Should().Be("001 — Acme");
        registry.Format(another).Should().Be("Entity #42");
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private static DisplayTemplateRegistry BuildRegistry(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var sp = services.BuildServiceProvider();
        return new DisplayTemplateRegistry(sp);
    }

    // ── Test types ──────────────────────────────────────────────────────
    internal sealed record SampleEntity(string Name, string Code)
    {
        public override string ToString() => $"SampleEntity({Code})";
    }

    internal sealed class SampleDisplayTemplate : IDisplayTemplate<SampleEntity>
    {
        public string Format(SampleEntity entity) => $"{entity.Code} — {entity.Name}";
    }

    internal sealed record AnotherEntity(int Id);

    internal sealed class AnotherDisplayTemplate : IDisplayTemplate<AnotherEntity>
    {
        public string Format(AnotherEntity entity) => $"Entity #{entity.Id}";
    }

    internal sealed class NullToStringEntity
    {
        public override string? ToString() => null;
    }
}
