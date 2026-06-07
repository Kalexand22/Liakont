namespace Liakont.Modules.Supervision.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Cycle de vie d'une alerte sur PostgreSQL réel (base du tenant) : persistance, lectures, auto-résolution
/// et acquittement opérateur. (INV-SUPERVISION-005, 006)
/// </summary>
[Collection("SupervisionIntegration")]
public sealed class AlertLifecycleIntegrationTests
{
    private const string Tenant = "acme";
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly TenantDatabase _db;

    public AlertLifecycleIntegrationTests(SupervisionDatabaseFixture fixture)
    {
        _db = fixture.CreateTenantDatabase();
    }

    [Fact]
    public async Task Insert_Then_Read_RoundTrips_The_Alert()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var alert = Alert.Raise(Tenant, "agent.mute", AlertSeverity.Critical, "L'agent ne répond plus.", Now);

        await store.InsertAsync(alert);

        var byId = await queries.GetByIdAsync(alert.Id);
        byId.Should().NotBeNull();
        byId!.TenantId.Should().Be(Tenant);
        byId.RuleKey.Should().Be("agent.mute");
        byId.Severity.Should().Be(nameof(AlertSeverity.Critical));
        byId.Detail.Should().Be("L'agent ne répond plus.");
        byId.IsActive.Should().BeTrue();

        var active = await queries.ListActiveAsync();
        active.Should().ContainSingle(a => a.Id == alert.Id);
    }

    [Fact]
    public async Task Resolved_Alert_Leaves_The_Active_List_But_Stays_In_Recent()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var alert = Alert.Raise(Tenant, "pa.rejects", AlertSeverity.Critical, null, Now);
        await store.InsertAsync(alert);

        var reloaded = await store.FindActiveByRuleAsync("pa.rejects");
        reloaded.Should().NotBeNull();
        reloaded!.Resolve(Now.AddHours(2));
        await store.ResolveAsync(reloaded);

        (await queries.ListActiveAsync()).Should().BeEmpty();
        var recent = await queries.ListRecentAsync(10);
        recent.Should().ContainSingle(a => a.Id == alert.Id);
        recent.Single(a => a.Id == alert.Id).ResolvedUtc.Should().NotBeNull();
        (await store.FindActiveByRuleAsync("pa.rejects")).Should().BeNull();
    }

    [Fact]
    public async Task Acknowledge_Records_Operator_Without_Resolving()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var ack = new AlertAcknowledgementService(store);
        var alert = Alert.Raise(Tenant, "agent.mute", AlertSeverity.Critical, null, Now);
        await store.InsertAsync(alert);

        var acknowledged = await ack.AcknowledgeAsync(alert.Id, "operator@instance");

        acknowledged.Should().BeTrue();
        var reloaded = await queries.GetByIdAsync(alert.Id);
        reloaded!.AcknowledgedBy.Should().Be("operator@instance");
        reloaded.AcknowledgedUtc.Should().NotBeNull();

        // Acquitter ne résout pas : l'alerte reste active.
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Acknowledge_Absent_Alert_Returns_False()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var ack = new AlertAcknowledgementService(store);

        var acknowledged = await ack.AcknowledgeAsync(Guid.NewGuid(), "operator@instance");

        acknowledged.Should().BeFalse();
    }
}
