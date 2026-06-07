namespace Liakont.Modules.Supervision.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Prouve que les mises à jour ciblées (<see cref="PostgresAlertStore.ResolveAsync"/> et
/// <see cref="PostgresAlertStore.AcknowledgeAsync"/>) n'effacent pas les colonnes de l'autre opération
/// en cas de snapshot périmé — absence de lost-update entre auto-résolution et acquittement opérateur.
/// </summary>
[Collection("SupervisionIntegration")]
public sealed class AlertConcurrencyIntegrationTests
{
    private const string Tenant = "acme";
    private const string RuleKey = "agent.mute";
    private const string Operator = "operator@instance";

    private static readonly DateTimeOffset T1 = new(2026, 6, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 6, 7, 10, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Raised = new(2026, 6, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly TenantDatabase _db;

    public AlertConcurrencyIntegrationTests(SupervisionDatabaseFixture fixture)
    {
        _db = fixture.CreateTenantDatabase();
    }

    [Fact]
    public async Task Acknowledge_With_Stale_Snapshot_Does_Not_Resurrect_A_Resolved_Alert()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var alert = Alert.Raise(Tenant, RuleKey, AlertSeverity.Critical, null, Raised);
        await store.InsertAsync(alert);

        // Snapshot périmé pour l'acquittement (alerte encore active à ce moment).
        var ackSnapshot = await store.GetByIdAsync(alert.Id);
        ackSnapshot.Should().NotBeNull();

        // Résolution concurrente via un chargement frais.
        var active = await store.FindActiveByRuleAsync(RuleKey);
        active.Should().NotBeNull();
        active!.Resolve(T1);
        await store.ResolveAsync(active);

        // Acquittement via le snapshot périmé (resolved_utc y est NULL).
        ackSnapshot!.Acknowledge(Operator, T2);
        await store.AcknowledgeAsync(ackSnapshot);

        // L'alerte doit être résolue ET acquittée — pas de résurrection.
        var result = await queries.GetByIdAsync(alert.Id);
        result.Should().NotBeNull();
        result!.ResolvedUtc.Should().NotBeNull();
        result.AcknowledgedBy.Should().Be(Operator);
    }

    [Fact]
    public async Task Resolve_With_Stale_Snapshot_Does_Not_Erase_An_Acknowledgement()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var alert = Alert.Raise(Tenant, RuleKey, AlertSeverity.Critical, null, Raised);
        await store.InsertAsync(alert);

        // Snapshot périmé pour la résolution, capturé AVANT l'acquittement (acknowledged_by y est NULL).
        var resolveSnapshot = await store.FindActiveByRuleAsync(RuleKey);
        resolveSnapshot.Should().NotBeNull();

        // Acquittement via un chargement séparé.
        var ackLoad = await store.GetByIdAsync(alert.Id);
        ackLoad.Should().NotBeNull();
        ackLoad!.Acknowledge(Operator, T1);
        await store.AcknowledgeAsync(ackLoad);

        // Résolution via le snapshot périmé (acknowledged_by y est NULL).
        resolveSnapshot!.Resolve(T2);
        await store.ResolveAsync(resolveSnapshot);

        // L'alerte doit être résolue ET l'acquittement doit être préservé — pas d'effacement.
        var result = await queries.GetByIdAsync(alert.Id);
        result.Should().NotBeNull();
        result!.ResolvedUtc.Should().NotBeNull();
        result.AcknowledgedBy.Should().Be(Operator);
    }
}
