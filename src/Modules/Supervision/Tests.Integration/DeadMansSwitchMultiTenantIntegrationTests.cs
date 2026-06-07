namespace Liakont.Modules.Supervision.Tests.Integration;

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Integration.Doubles;
using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Jobs;
using Xunit;

/// <summary>
/// Dead-man's switch BOUT-EN-BOUT : le VRAI <see cref="TenantJobRunner"/> (SOL06) parcourt DEUX bases
/// tenant réelles via un <see cref="ITenantScopeFactory"/> de test. Chaque tenant est évalué dans SA base
/// (isolation) ; un tenant en échec n'empêche pas l'autre. (INV-SUPERVISION-002, 007, 008)
/// </summary>
[Collection("SupervisionIntegration")]
public sealed class DeadMansSwitchMultiTenantIntegrationTests
{
    private readonly SupervisionDatabaseFixture _fixture;

    public DeadMansSwitchMultiTenantIntegrationTests(SupervisionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Runner_EvaluatesEachTenant_InItsOwnDatabase()
    {
        var alphaDb = _fixture.CreateTenantDatabase();
        var betaDb = _fixture.CreateTenantDatabase();

        var alphaQueries = new PostgresAlertQueries(alphaDb.ConnectionFactory);
        var betaQueries = new PostgresAlertQueries(betaDb.ConnectionFactory);

        var engines = new Dictionary<string, IAlertEvaluationService>
        {
            ["alpha"] = EngineFor(alphaDb, new StubAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true)),
            ["beta"] = EngineFor(betaDb, new StubAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true)),
        };

        var runner = BuildRunner(engines, "alpha", "beta");

        var summary = await runner.RunForAllTenantsAsync(new SupervisionEvaluationTenantJob());

        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(2);
        summary.FailedCount.Should().Be(0);

        var alphaAlerts = await alphaQueries.ListActiveAsync();
        var betaAlerts = await betaQueries.ListActiveAsync();
        alphaAlerts.Should().ContainSingle().Which.TenantId.Should().Be("alpha");
        betaAlerts.Should().ContainSingle().Which.TenantId.Should().Be("beta");
    }

    [Fact]
    public async Task A_Failing_Tenant_Does_Not_Stop_The_Others()
    {
        var alphaDb = _fixture.CreateTenantDatabase();
        var betaDb = _fixture.CreateTenantDatabase();

        var betaQueries = new PostgresAlertQueries(betaDb.ConnectionFactory);

        var engines = new Dictionary<string, IAlertEvaluationService>
        {
            // alpha : règle en panne → le job tenant lève → le runner isole l'échec.
            ["alpha"] = EngineFor(alphaDb, new StubAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true) { ThrowOnEvaluate = true }),
            ["beta"] = EngineFor(betaDb, new StubAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true)),
        };

        var runner = BuildRunner(engines, "alpha", "beta");

        var summary = await runner.RunForAllTenantsAsync(new SupervisionEvaluationTenantJob());

        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(1);
        summary.FailedCount.Should().Be(1);
        summary.Failures.Should().ContainSingle(f => f.TenantId == "alpha");

        // beta a tout de même été évalué malgré l'échec d'alpha.
        (await betaQueries.ListActiveAsync()).Should().ContainSingle().Which.TenantId.Should().Be("beta");
    }

    private static AlertEvaluationService EngineFor(TenantDatabase db, StubAlertRule rule) =>
        new(new[] { rule }, new PostgresAlertStore(db.ConnectionFactory));

    private static TenantJobRunner BuildRunner(
        IReadOnlyDictionary<string, IAlertEvaluationService> engines,
        params string[] tenantIds)
    {
        var tenants = new TenantDto[tenantIds.Length];
        for (var i = 0; i < tenantIds.Length; i++)
        {
            tenants[i] = ListTenantQueries.ActiveTenant(tenantIds[i]);
        }

        var queries = new ListTenantQueries(tenants);
        var scopeFactory = new MapTenantScopeFactory(engines);
        return new TenantJobRunner(queries, scopeFactory, NullLogger<TenantJobRunner>.Instance);
    }
}
