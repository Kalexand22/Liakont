namespace Stratum.Common.Infrastructure.Tests.Integration.DataIsolation;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;
using Xunit;

public sealed class CompanyFilterDiTests
{
    [Fact]
    public void AddStratumCompanyFilter_Should_ResolveICompanyFilter()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorContextAccessor>(_ => new TestActorContextAccessor(Guid.NewGuid()));
        services.AddStratumCompanyFilter();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filter = scope.ServiceProvider.GetService<ICompanyFilter>();

        filter.Should().NotBeNull();
        filter!.GetType().Name.Should().Be("CompanyFilter");
    }

    [Fact]
    public void AddStratumCompanyFilter_Should_ReturnCompanyId_When_ContextHasCompanyId()
    {
        var expectedId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddScoped<IActorContextAccessor>(_ => new TestActorContextAccessor(expectedId));
        services.AddStratumCompanyFilter();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filter = scope.ServiceProvider.GetRequiredService<ICompanyFilter>();
        var result = filter.GetRequiredCompanyId();

        result.Should().Be(expectedId);
    }

    private sealed class TestActorContextAccessor : IActorContextAccessor
    {
        public TestActorContextAccessor(Guid? companyId)
        {
            Current = new TestActorContext(companyId);
        }

        public IActorContext Current { get; }
    }

    private sealed class TestActorContext : IActorContext
    {
        public TestActorContext(Guid? companyId) => CompanyId = companyId;

        public Guid UserId => Guid.NewGuid();

        public Guid CorrelationId => Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => null;

        public string? Email => null;

        public Guid? CompanyId { get; }

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
