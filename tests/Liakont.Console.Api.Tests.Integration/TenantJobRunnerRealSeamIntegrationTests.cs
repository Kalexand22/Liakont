namespace Liakont.Console.Api.Tests.Integration;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// RDL08 / A6-di-2 : exerce le SEAM DE PRODUCTION (Host <c>TenantScopeFactory</c>) bout-en-bout sur deux vraies
/// bases tenant, via le composition root RÉEL (<see cref="ConsoleApiFactory"/>, <c>AppBootstrap</c> +
/// <c>AddStratumMultiTenancy</c> + <c>AddTenantJobs</c>) — et non plus un double de test. Prouve que
/// <see cref="ITenantJobRunner.RunForAllTenantsAsync"/> bascule la connexion vers la base de CHAQUE tenant
/// (current_database() distinct), donc qu'un fan-out écrit dans la bonne base (pas de fuite cross-tenant).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantJobRunnerRealSeamIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public TenantJobRunnerRealSeamIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RunForAllTenants_Through_Real_Host_Seam_Routes_To_Each_Tenant_Database()
    {
        using var scope = _factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var job = new ObserveDatabaseTenantJob();

        await runner.RunForAllTenantsAsync(job);

        // The REAL Host TenantScopeFactory (not a test double) established each tenant on its scope, so the
        // scoped IConnectionFactory routed to that tenant's PHYSICAL database.
        job.ObservedDatabaseByTenant.Should().ContainKey(ConsoleApiFactory.TenantA);
        job.ObservedDatabaseByTenant[ConsoleApiFactory.TenantA].Should().Be("tc_tenant_a");
        job.ObservedDatabaseByTenant.Should().ContainKey(ConsoleApiFactory.TenantB);
        job.ObservedDatabaseByTenant[ConsoleApiFactory.TenantB].Should().Be("tc_tenant_b");
    }

    /// <summary>
    /// Read-only fan-out job: records <c>current_database()</c> per tenant. No writes, so it does not pollute
    /// the seeded data other suites assert on (the fan-out runs over every active tenant of the harness).
    /// </summary>
    private sealed class ObserveDatabaseTenantJob : ITenantJob
    {
        public string Name => "test.rdl08.observe-db";

        public ConcurrentDictionary<string, string> ObservedDatabaseByTenant { get; } = new();

        public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
        {
            var factory = context.Services.GetRequiredService<IConnectionFactory>();
            using var conn = await factory.OpenAsync(cancellationToken);
            var database = await conn.QuerySingleAsync<string>("SELECT current_database()");
            ObservedDatabaseByTenant[context.TenantId] = database;
        }
    }
}
