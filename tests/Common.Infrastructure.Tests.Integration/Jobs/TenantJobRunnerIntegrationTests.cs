namespace Stratum.Common.Infrastructure.Tests.Integration.Jobs;

using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Jobs;
using Stratum.Common.Infrastructure.Tests.Integration.Portal;
using Xunit;

/// <summary>
/// Proves the SOL06 acceptance criteria against two real tenant databases (Testcontainers):
/// the runner switches the connection per tenant, writes land in the right tenant database
/// (no cross-tenant leak), and a failure for one tenant does not stop the others.
/// </summary>
public sealed class TenantJobRunnerIntegrationTests : IAsyncLifetime
{
    private readonly MultiTenantFixture _fixture = new();
    private ITenantConnectionFactory _tenantConnectionFactory = null!;
    private ITenantScopeFactory _scopeFactory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _tenantConnectionFactory = _fixture.CreateTenantConnectionFactory();
        _scopeFactory = new TestTenantScopeFactory(_tenantConnectionFactory);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RunForAllTenants_Should_SwitchConnectionPerTenant_AndKeepDataIsolated()
    {
        var runner = new TenantJobRunner(
            _fixture.CreateTenantQueries(),
            _scopeFactory,
            NullLogger<TenantJobRunner>.Instance);
        var job = new WritePartyTenantJob();

        var summary = await runner.RunForAllTenantsAsync(job);

        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(2);
        summary.FailedCount.Should().Be(0);

        // The connection was switched to each tenant's own database.
        job.ObservedDatabaseByTenant[MultiTenantFixture.TenantA].Should().Be("stratum_tenant_a");
        job.ObservedDatabaseByTenant[MultiTenantFixture.TenantB].Should().Be("stratum_tenant_b");

        // Each write landed only in its own tenant database — no cross-tenant leak.
        (await CountPartiesAsync(MultiTenantFixture.TenantA)).Should().Be(1);
        (await SinglePartyNameAsync(MultiTenantFixture.TenantA)).Should().Be(MultiTenantFixture.TenantA);
        (await CountPartiesAsync(MultiTenantFixture.TenantB)).Should().Be(1);
        (await SinglePartyNameAsync(MultiTenantFixture.TenantB)).Should().Be(MultiTenantFixture.TenantB);
    }

    [Fact]
    public async Task RunForAllTenants_Should_IsolateFailure_AcrossTenantDatabases()
    {
        var runner = new TenantJobRunner(
            _fixture.CreateTenantQueries(),
            _scopeFactory,
            NullLogger<TenantJobRunner>.Instance);

        // The job throws for tenant-b (before it writes); tenant-a must still be processed.
        var job = new WritePartyTenantJob(throwFor: id => id == MultiTenantFixture.TenantB);

        var summary = await runner.RunForAllTenantsAsync(job);

        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(1);
        summary.FailedCount.Should().Be(1);
        summary.Failures.Should().ContainSingle()
            .Which.TenantId.Should().Be(MultiTenantFixture.TenantB);

        // tenant-a completed despite tenant-b failing; tenant-b wrote nothing.
        (await CountPartiesAsync(MultiTenantFixture.TenantA)).Should().Be(1);
        (await CountPartiesAsync(MultiTenantFixture.TenantB)).Should().Be(0);
    }

    private async Task<long> CountPartiesAsync(string tenantId)
    {
        using var conn = await _tenantConnectionFactory.OpenAsync(tenantId);
        return await conn.QuerySingleAsync<long>("SELECT count(*) FROM party.parties");
    }

    private async Task<string?> SinglePartyNameAsync(string tenantId)
    {
        using var conn = await _tenantConnectionFactory.OpenAsync(tenantId);
        return await conn.QuerySingleOrDefaultAsync<string?>("SELECT legal_name FROM party.parties");
    }

    /// <summary>
    /// Test double for the seam the Host provides in production: builds a scope whose
    /// <see cref="IConnectionFactory"/> is routed to the requested tenant via the real
    /// <see cref="TenantScopedConnectionFactory"/> + <see cref="ITenantConnectionFactory"/>.
    /// </summary>
    private sealed class TestTenantScopeFactory : ITenantScopeFactory
    {
        private readonly ITenantConnectionFactory _tenantConnectionFactory;

        public TestTenantScopeFactory(ITenantConnectionFactory tenantConnectionFactory)
            => _tenantConnectionFactory = tenantConnectionFactory;

        public ITenantScope Create(string tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_tenantConnectionFactory);
            services.AddSingleton<ITenantContext>(new FixedTenantContext(tenantId));
            services.AddScoped<IConnectionFactory, TenantScopedConnectionFactory>();
            var provider = services.BuildServiceProvider();
            return new TestTenantScope(provider, provider.CreateAsyncScope(), tenantId);
        }
    }

    private sealed class TestTenantScope : ITenantScope
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public TestTenantScope(ServiceProvider provider, AsyncServiceScope scope, string tenantId)
        {
            _provider = provider;
            _scope = scope;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IServiceProvider Services => _scope.ServiceProvider;

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(string tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class WritePartyTenantJob : ITenantJob
    {
        private readonly Func<string, bool>? _throwFor;

        public WritePartyTenantJob(Func<string, bool>? throwFor = null) => _throwFor = throwFor;

        public string Name => "test.write-party";

        public Dictionary<string, string> ObservedDatabaseByTenant { get; } = [];

        public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
        {
            var factory = context.Services.GetRequiredService<IConnectionFactory>();
            using var conn = await factory.OpenAsync(cancellationToken);

            var database = await conn.QuerySingleAsync<string>("SELECT current_database()");
            ObservedDatabaseByTenant[context.TenantId] = database;

            if (_throwFor is not null && _throwFor(context.TenantId))
            {
                throw new InvalidOperationException($"boom for {context.TenantId}");
            }

            await conn.ExecuteAsync(
                "INSERT INTO party.parties (legal_name, is_public, is_active) VALUES (@Name, false, true)",
                new { Name = context.TenantId });
        }
    }
}
