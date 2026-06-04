namespace Liakont.Host.Tests.Unit.MultiTenancy;

using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Exercises the production tenant-scope seam (SOL06): TenantScopeFactory must establish the tenant
/// on the scope's ITenantContext, using the real AddStratumMultiTenancy registration. Guards against
/// a DI regression where ITenantContext stops sharing the scoped MutableTenantContext instance —
/// which would silently route tenant jobs to the system database (cross-tenant leak).
/// </summary>
public sealed class TenantScopeFactoryTests
{
    [Fact]
    public async Task Create_Should_Establish_Tenant_On_ScopedTenantContext()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();

        await using var scope = factory.Create("tenant-x");

        scope.TenantId.Should().Be("tenant-x");
        var context = scope.Services.GetRequiredService<ITenantContext>();
        context.TenantId.Should().Be("tenant-x");
        context.IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Isolate_Tenants_Across_Scopes()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();

        await using var scopeA = factory.Create("tenant-a");
        await using var scopeB = factory.Create("tenant-b");

        scopeA.Services.GetRequiredService<ITenantContext>().TenantId.Should().Be("tenant-a");
        scopeB.Services.GetRequiredService<ITenantContext>().TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task Create_Should_Share_One_ScopedTenantContext_Instance()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();

        await using var scope = factory.Create("tenant-x");

        var first = scope.Services.GetRequiredService<ITenantContext>();
        var second = scope.Services.GetRequiredService<ITenantContext>();
        second.Should().BeSameAs(first);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumMultiTenancy(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
}
