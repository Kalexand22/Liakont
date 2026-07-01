namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Contracts.Consultation;
using Liakont.Modules.Ged.Infrastructure.Consultation;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests base-réelle de <see cref="PostgresConsultationAuditWriter"/> (GED13, F19 §6.6, ADR-0036) : une entrée
/// par action (identité + corrélation résolues server-side), MASQUAGE server-side de <c>query_text</c> et des
/// valeurs confidentielles de <c>detail</c> (anti-oracle, §6.5), les DEUX régimes de robustesse (best-effort ne
/// casse pas la lecture / probant fail-closed), et l'ISOLATION cross-tenant (≥ 2 bases).
/// </summary>
[Collection("GedIntegration")]
public sealed class ConsultationAuditWriterIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public ConsultationAuditWriterIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────── écriture : une entrée par action, identité/corrélation server-side ───────────────────────

    [Fact]
    public async Task Write_persists_one_entry_with_server_side_actor_and_correlation()
    {
        var factory = _fixture.CreateTenantDatabase();
        var actorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var writer = CreateWriter(factory, actorId, correlationId, ConsultationAuditMode.BestEffort);

        await writer.WriteAsync(new ConsultationLogEntry
        {
            Action = ConsultationAction.Search,
            QueryText = "facture 2026",
            ResultCount = 5,
            Detail = new Dictionary<string, string?> { ["public"] = "v" },
        });

        using var connection = await factory.OpenAsync();
        var row = await connection.QueryFirstAsync<ConsultationRow>(
            "SELECT actor_id AS ActorId, action AS Action, query_text AS QueryText, result_count AS ResultCount, "
            + "correlation_id AS CorrelationId, detail->>'public' AS PublicDetail "
            + "FROM ged_index.consultation_log");

        row.ActorId.Should().Be(actorId.ToString());
        row.Action.Should().Be("search");
        row.QueryText.Should().Be("facture 2026");
        row.ResultCount.Should().Be(5);
        row.CorrelationId.Should().Be(correlationId); // corrélation retombée sur le contexte d'acteur
        row.PublicDetail.Should().Be("v");
    }

    [Fact]
    public async Task Write_maps_each_action_to_its_closed_db_value()
    {
        var factory = _fixture.CreateTenantDatabase();
        var writer = CreateWriter(factory, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        foreach (var action in Enum.GetValues<ConsultationAction>())
        {
            await writer.WriteAsync(new ConsultationLogEntry { Action = action });
        }

        using var connection = await factory.OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.consultation_log");
        count.Should().Be(Enum.GetValues<ConsultationAction>().Length);
    }

    // ─────────────────────── masquage confidentiel server-side (§6.5, anti-oracle) ───────────────────────

    [Fact]
    public async Task Masks_query_text_and_confidential_detail_when_targeting_a_confidential_axis_without_the_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        using (var setup = await factory.OpenAsync())
        {
            await InsertAxisAsync(setup, "secret", confidential: true);
            await InsertAxisAsync(setup, "public", confidential: false);
        }

        var writer = CreateWriter(factory, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        await writer.WriteAsync(new ConsultationLogEntry
        {
            Action = ConsultationAction.Search,
            QueryText = "valeur-très-confidentielle",
            Detail = new Dictionary<string, string?> { ["secret"] = "42", ["public"] = "ok" },
            TargetedAxisCodes = ["secret", "public"],
            ActorHasConfidentialAccess = false,
        });

        using var connection = await factory.OpenAsync();
        var row = await connection.QueryFirstAsync<MaskRow>(
            "SELECT query_text AS QueryText, detail->>'secret' AS Secret, detail->>'public' AS Public "
            + "FROM ged_index.consultation_log");

        row.QueryText.Should().Be(PostgresConsultationAuditWriter.RedactedMarker);
        row.Secret.Should().Be(PostgresConsultationAuditWriter.RedactedMarker);
        row.Public.Should().Be("ok"); // valeur non confidentielle intacte
    }

    [Fact]
    public async Task Does_not_mask_when_actor_has_the_confidential_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        using (var setup = await factory.OpenAsync())
        {
            await InsertAxisAsync(setup, "secret", confidential: true);
        }

        var writer = CreateWriter(factory, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        await writer.WriteAsync(new ConsultationLogEntry
        {
            Action = ConsultationAction.Search,
            QueryText = "valeur-en-clair",
            Detail = new Dictionary<string, string?> { ["secret"] = "42" },
            TargetedAxisCodes = ["secret"],
            ActorHasConfidentialAccess = true,
        });

        using var connection = await factory.OpenAsync();
        var row = await connection.QueryFirstAsync<MaskRow>(
            "SELECT query_text AS QueryText, detail->>'secret' AS Secret, null AS Public "
            + "FROM ged_index.consultation_log");

        row.QueryText.Should().Be("valeur-en-clair");
        row.Secret.Should().Be("42");
    }

    [Fact]
    public async Task Does_not_mask_when_targeted_axis_is_not_confidential()
    {
        var factory = _fixture.CreateTenantDatabase();
        using (var setup = await factory.OpenAsync())
        {
            await InsertAxisAsync(setup, "public", confidential: false);
        }

        var writer = CreateWriter(factory, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        await writer.WriteAsync(new ConsultationLogEntry
        {
            Action = ConsultationAction.Search,
            QueryText = "recherche-banale",
            TargetedAxisCodes = ["public"],
            ActorHasConfidentialAccess = false,
        });

        using var connection = await factory.OpenAsync();
        var queryText = await connection.ExecuteScalarAsync<string>(
            "SELECT query_text FROM ged_index.consultation_log");

        queryText.Should().Be("recherche-banale");
    }

    [Fact]
    public async Task Masks_query_text_when_targeting_a_confidential_entity_type_without_the_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        using (var setup = await factory.OpenAsync())
        {
            await InsertEntityTypeAsync(setup, "dossier_secret", confidential: true);
        }

        var writer = CreateWriter(factory, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        await writer.WriteAsync(new ConsultationLogEntry
        {
            Action = ConsultationAction.ExploreEntity,
            QueryText = "nom-confidentiel",
            EntityId = Guid.NewGuid(),
            TargetedEntityTypeCode = "dossier_secret",
            ActorHasConfidentialAccess = false,
        });

        using var connection = await factory.OpenAsync();
        var queryText = await connection.ExecuteScalarAsync<string>(
            "SELECT query_text FROM ged_index.consultation_log");

        queryText.Should().Be(PostgresConsultationAuditWriter.RedactedMarker);
    }

    // ─────────────────────── régimes de robustesse (ADR-0036 §3) ───────────────────────

    [Fact]
    public async Task Best_effort_does_not_throw_when_the_write_fails()
    {
        // Fabrique de connexion défaillante → l'écriture échoue ; en best-effort la LECTURE n'est pas cassée.
        var writer = CreateWriter(new ThrowingConnectionFactory(), Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);

        Func<Task> act = () => writer.WriteAsync(new ConsultationLogEntry { Action = ConsultationAction.Search });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Evidential_throws_a_consultation_audit_exception_when_the_write_fails()
    {
        // Régime PROBANT : l'échec de trace est fail-closed (jamais un Warning noyé) — l'appelant refuse l'accès.
        var writer = CreateWriter(new ThrowingConnectionFactory(), Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.Evidential);

        Func<Task> act = () => writer.WriteAsync(new ConsultationLogEntry { Action = ConsultationAction.Search });

        await act.Should().ThrowAsync<ConsultationAuditException>();
    }

    // ─────────────────────── isolation cross-tenant (CLAUDE.md n°9) ───────────────────────

    [Fact]
    public async Task Consultation_is_isolated_per_tenant()
    {
        var tenantA = _fixture.CreateTenantDatabase();
        var tenantB = _fixture.CreateTenantDatabase();

        var writerB = CreateWriter(tenantB, Guid.NewGuid(), Guid.NewGuid(), ConsultationAuditMode.BestEffort);
        await writerB.WriteAsync(new ConsultationLogEntry { Action = ConsultationAction.Search, QueryText = "chez B" });

        using var connectionA = await tenantA.OpenAsync();
        var countA = await connectionA.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.consultation_log");
        using var connectionB = await tenantB.OpenAsync();
        var countB = await connectionB.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.consultation_log");

        countA.Should().Be(0); // la consultation de B est invisible depuis A
        countB.Should().Be(1);
    }

    // ─────────────────────── Helpers & doubles ───────────────────────

    private static PostgresConsultationAuditWriter CreateWriter(
        IConnectionFactory connectionFactory,
        Guid actorId,
        Guid correlationId,
        ConsultationAuditMode mode) =>
        new(
            connectionFactory,
            new FakeActorContextAccessor(actorId, correlationId),
            new StubModeProvider(mode),
            NullLogger<PostgresConsultationAuditWriter>.Instance);

    private static async Task InsertAxisAsync(IDbConnection connection, string code, bool confidential) =>
        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type, is_confidential) "
            + "VALUES (@Code, @Code, 'string', @Confidential)",
            new { Code = code, Confidential = confidential });

    private static async Task InsertEntityTypeAsync(IDbConnection connection, string code, bool confidential) =>
        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.entity_types (code, label, is_confidential) VALUES (@Code, @Code, @Confidential)",
            new { Code = code, Confidential = confidential });

    private sealed class ConsultationRow
    {
        public string ActorId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string? QueryText { get; set; }

        public int? ResultCount { get; set; }

        public Guid? CorrelationId { get; set; }

        public string? PublicDetail { get; set; }
    }

    private sealed class MaskRow
    {
        public string? QueryText { get; set; }

        public string? Secret { get; set; }

        public string? Public { get; set; }
    }

    private sealed class StubModeProvider : IConsultationAuditModeProvider
    {
        private readonly ConsultationAuditMode _mode;

        public StubModeProvider(ConsultationAuditMode mode) => _mode = mode;

        public ValueTask<ConsultationAuditMode> GetModeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_mode);
    }

    private sealed class ThrowingConnectionFactory : IConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("connexion indisponible (test).");
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid userId, Guid correlationId) =>
            Current = new FakeActorContext(userId, correlationId);

        public IActorContext Current { get; }
    }

    private sealed class FakeActorContext : IActorContext
    {
        public FakeActorContext(Guid userId, Guid correlationId)
        {
            UserId = userId;
            CorrelationId = correlationId;
        }

        public Guid UserId { get; }

        public Guid CorrelationId { get; }

        public bool IsAuthenticated => true;

        public string? DisplayName => "Testeur";

        public string? Email => "test@exemple.fr";

        public Guid? CompanyId => null;

        public string? Timezone => "Europe/Paris";

        public string? Language => "fr";

        public string? TenantId => "tenant-test";
    }
}
