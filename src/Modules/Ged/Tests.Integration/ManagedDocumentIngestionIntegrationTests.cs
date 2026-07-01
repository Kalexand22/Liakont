namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Contracts.DTOs;
using Liakont.Modules.Ged.Contracts.Events;
using Liakont.Modules.Ged.Domain.Mapping;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Ingestion;
using Liakont.Modules.Ged.Infrastructure.Mapping;
using Liakont.Modules.Ged.Tests.Integration.Doubles;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Xunit;

/// <summary>
/// Tests d'intégration base RÉELLE (Testcontainers PostgreSQL) de l'ingestion GÉNÉRIQUE GED (GED05b, F19 §2.4/§4.3).
/// Prouvent : (1) l'écriture ATOMIQUE registre GED + événement <c>ManagedDocumentReceivedV1</c> en base système +
/// l'anti-doublon idempotent (Duplicate) ; (2) le drainage de l'événement et l'indexation par le consommateur
/// (ManagedDocument + liens d'axe), avec replay NO-OP (RL-04) ; (3) l'écriture UNE SEULE FOIS sous livraisons
/// CONCURRENTES (RL-04, un test séquentiel serait un faux-vert) ; (4) le DÉFÉREMENT sans profil validé (INV-GED-05) ;
/// (5) le tenant-scoping par la connexion (≥ 2 bases). AUCUN Document/état fiscal n'est atteint.
/// </summary>
[Collection("GedIntegration")]
public sealed class ManagedDocumentIngestionIntegrationTests
{
    private const string TenantA = "tenant-ged-a";
    private const string TenantB = "tenant-ged-b";

    private static readonly JsonSerializerOptions OutboxJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GedDatabaseFixture _fixture;

    public ManagedDocumentIngestionIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Accepted_document_writes_registry_and_event_atomically_and_reingestion_is_a_duplicate()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        var handler = BuildHandler(factory, staging);

        var first = await IngestAsync(handler, TenantA, "SRC-1", "NOTE", new Dictionary<string, string> { ["d"] = "2026-06-30" });

        first.Results.Single().Status.Should().Be(ManagedDocumentPushStatus.Accepted);
        (await CountRegistryAsync(factory, TenantA)).Should().Be(1, "le registre GED enregistre la réception (base système)");
        (await CountGedEventsAsync(factory)).Should().Be(1, "l'événement ManagedDocumentReceivedV1 est écrit ATOMIQUEMENT avec le registre");

        // Ré-ingestion du MÊME contenu : doublon strict — aucune seconde ligne de registre, aucun second événement.
        var second = await IngestAsync(handler, TenantA, "SRC-1", "NOTE", new Dictionary<string, string> { ["d"] = "2026-06-30" });

        second.Results.Single().Status.Should().Be(ManagedDocumentPushStatus.Duplicate);
        (await CountRegistryAsync(factory, TenantA)).Should().Be(1, "une ré-ingestion idempotente n'ajoute rien (anti-doublon tenant, hash)");
        (await CountGedEventsAsync(factory)).Should().Be(1, "un doublon ne publie pas d'événement");
    }

    [Fact]
    public async Task Consumer_indexes_a_mapped_document_and_a_replay_is_a_no_op()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        await SeedAxisAsync(factory, "d", "string");
        await SeedValidatedProfileAsync(factory, "NOTE", "d", "$.fields.d");
        var handler = BuildHandler(factory, staging);
        var consumer = BuildConsumer(staging, (TenantA, factory));

        await IngestAsync(handler, TenantA, "SRC-1", "NOTE", new Dictionary<string, string> { ["d"] = "2026-06-30" });
        var evt = await DrainSingleGedEventAsync(factory);

        await consumer.HandleAsync(evt, CancellationToken.None);

        (await StatusAsync(factory, evt.Payload.ManagedDocumentId)).Should().Be("indexed");
        (await CountAxisLinksAsync(factory)).Should().Be(1, "l'axe mappé est écrit une fois");
        (await AxisValueStringAsync(factory)).Should().Be("2026-06-30");

        // Replay at-least-once : le document est déjà indexé → aucune écriture supplémentaire (RL-04).
        await consumer.HandleAsync(evt, CancellationToken.None);

        (await CountManagedDocumentsAsync(factory)).Should().Be(1, "un replay ne crée pas de second ManagedDocument");
        (await CountAxisLinksAsync(factory)).Should().Be(1, "un replay ne redouble pas les liens (ON CONFLICT + garde de statut)");
    }

    [Fact]
    public async Task Concurrent_deliveries_write_links_exactly_once()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        await SeedAxisAsync(factory, "d", "string");
        await SeedValidatedProfileAsync(factory, "NOTE", "d", "$.fields.d");
        var handler = BuildHandler(factory, staging);
        var consumer = BuildConsumer(staging, (TenantA, factory));

        await IngestAsync(handler, TenantA, "SRC-1", "NOTE", new Dictionary<string, string> { ["d"] = "2026-06-30" });
        var evt = await DrainSingleGedEventAsync(factory);

        // Deux livraisons SIMULTANÉES du même événement : la garde de concurrence (verrou consultatif + statut) doit
        // n'écrire les liens qu'UNE fois. Un test séquentiel serait un faux-vert (RL-04, §8).
        await Task.WhenAll(
            Task.Run(() => consumer.HandleAsync(evt, CancellationToken.None)),
            Task.Run(() => consumer.HandleAsync(evt, CancellationToken.None)));

        (await CountManagedDocumentsAsync(factory)).Should().Be(1, "deux livraisons simultanées ⇒ un seul ManagedDocument");
        (await CountAxisLinksAsync(factory)).Should().Be(1, "deux livraisons simultanées ⇒ liens écrits UNE SEULE FOIS");
    }

    [Fact]
    public async Task Document_without_a_validated_profile_is_deferred_with_a_reason()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        var handler = BuildHandler(factory, staging);
        var consumer = BuildConsumer(staging, (TenantA, factory));

        await IngestAsync(handler, TenantA, "SRC-1", "TYPE_SANS_PROFIL", new Dictionary<string, string> { ["d"] = "x" });
        var evt = await DrainSingleGedEventAsync(factory);

        await consumer.HandleAsync(evt, CancellationToken.None);

        (await StatusAsync(factory, evt.Payload.ManagedDocumentId)).Should().Be("deferred", "aucun profil validé ⇒ deferred, jamais deviné (INV-GED-05)");
        (await DeferReasonAsync(factory, evt.Payload.ManagedDocumentId)).Should().Contain("profil", "le motif français est actionnable (n°12, visible console)");
        (await CountAxisLinksAsync(factory)).Should().Be(0, "un document déféré n'écrit aucun lien");
        (await CountManagedDocumentsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Consumer_resolves_declared_entities_and_relations_into_graph_links()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        await SeedAxisAsync(factory, "d", "string");
        await SeedEntityTypeAsync(factory, "partner", identityKey: "ref");
        await SeedGraphProfileAsync(factory, "DEAL");
        var handler = BuildHandler(factory, staging);
        var consumer = BuildConsumer(staging, (TenantA, factory));

        var document = new IngestedDocumentDto(
            "SRC-1",
            "DEAL",
            sourceFields: new Dictionary<string, string> { ["d"] = "2026-01-01", ["rel"] = "P-99" },
            sourceEntities: new[] { new RawEntityHint("p", "P-1", "Partner One") });
        await IngestDocumentAsync(handler, TenantA, document);
        var evt = await DrainSingleGedEventAsync(factory);

        await consumer.HandleAsync(evt, CancellationToken.None);

        (await StatusAsync(factory, evt.Payload.ManagedDocumentId)).Should().Be("indexed");
        (await CountEntityInstancesAsync(factory)).Should().Be(2, "l'entité déclarée (P-1) et la cible de relation (P-99) sont résolues/créées");
        (await CountDocumentEntityLinksAsync(factory)).Should().Be(2, "un lien role=type (partner) + un lien role=Kind (concerne)");
        (await CountEntityCreatedLogAsync(factory)).Should().Be(2, "chaque création d'entité est tracée append-only (entity_created)");

        // Replay : idempotent (garde de statut) — aucune entité ni lien redoublé.
        await consumer.HandleAsync(evt, CancellationToken.None);

        (await CountEntityInstancesAsync(factory)).Should().Be(2);
        (await CountDocumentEntityLinksAsync(factory)).Should().Be(2);
    }

    [Fact]
    public async Task Document_referencing_an_unknown_entity_type_is_deferred()
    {
        var factory = _fixture.CreateTenantDatabase();
        var staging = new InMemoryPayloadStagingStore();
        await SeedAxisAsync(factory, "d", "string");
        await SeedGraphProfileAsync(factory, "DEAL"); // profil réfère le type d'entité « partner », NON déclaré au catalogue
        var handler = BuildHandler(factory, staging);
        var consumer = BuildConsumer(staging, (TenantA, factory));

        var document = new IngestedDocumentDto(
            "SRC-1",
            "DEAL",
            sourceFields: new Dictionary<string, string> { ["d"] = "2026-01-01", ["rel"] = "P-99" },
            sourceEntities: new[] { new RawEntityHint("p", "P-1", "Partner One") });
        await IngestDocumentAsync(handler, TenantA, document);
        var evt = await DrainSingleGedEventAsync(factory);

        await consumer.HandleAsync(evt, CancellationToken.None);

        (await StatusAsync(factory, evt.Payload.ManagedDocumentId)).Should().Be("deferred", "un type d'entité inconnu défère (jamais deviner, n°3)");
        (await DeferReasonAsync(factory, evt.Payload.ManagedDocumentId)).Should().Contain("partner", "le motif nomme le type d'entité manquant");
        (await CountEntityInstancesAsync(factory)).Should().Be(0, "aucune entité créée quand le document est déféré");
        (await CountDocumentEntityLinksAsync(factory)).Should().Be(0);
        (await CountAxisLinksAsync(factory)).Should().Be(0);
    }

    [Fact]
    public async Task Indexing_is_scoped_to_the_tenant_database()
    {
        var tenantA = _fixture.CreateTenantDatabase();
        var tenantB = _fixture.CreateTenantDatabase();
        await SeedAxisAsync(tenantA, "d", "string");
        await SeedValidatedProfileAsync(tenantA, "NOTE", "d", "$.fields.d");
        var staging = new InMemoryPayloadStagingStore();
        var handler = BuildHandler(tenantA, staging);
        var consumer = BuildConsumer(staging, (TenantA, tenantA), (TenantB, tenantB));

        await IngestAsync(handler, TenantA, "SRC-1", "NOTE", new Dictionary<string, string> { ["d"] = "2026-06-30" });
        var evt = await DrainSingleGedEventAsync(tenantA);
        await consumer.HandleAsync(evt, CancellationToken.None);

        (await CountManagedDocumentsAsync(tenantA)).Should().Be(1, "l'index est écrit dans la base du tenant A");
        (await CountManagedDocumentsAsync(tenantB)).Should().Be(0, "aucune donnée n'a fui vers la base du tenant B (tenant-scopé par la connexion, n°9)");
    }

    // NpgsqlConnectionFactory implémente IConnectionFactory ET ISystemConnectionFactory : en test, la MÊME base sert de
    // base « système » (registre GED + outbox) et de base « tenant » (index) — cf. GedDatabaseFixture.
    private static IngestManagedDocumentBatchHandler BuildHandler(IConnectionFactory systemFactory, InMemoryPayloadStagingStore staging) =>
        new(
            new PostgresGedReceivedDocumentUnitOfWorkFactory((ISystemConnectionFactory)systemFactory, new OutboxWriter(NullLogger<OutboxWriter>.Instance)),
            staging,
            NullLogger<IngestManagedDocumentBatchHandler>.Instance);

    private static ManagedDocumentReceivedConsumer BuildConsumer(
        InMemoryPayloadStagingStore staging,
        params (string TenantId, IConnectionFactory Factory)[] tenants)
    {
        var map = tenants.ToDictionary(t => t.TenantId, t => t.Factory, StringComparer.Ordinal);
        return new ManagedDocumentReceivedConsumer(
            new StubTenantScopeFactory(map, staging),
            NullLogger<ManagedDocumentReceivedConsumer>.Instance);
    }

    private static Task<ManagedDocumentBatchResultDto> IngestAsync(
        IngestManagedDocumentBatchHandler handler,
        string tenantId,
        string sourceReference,
        string documentType,
        IReadOnlyDictionary<string, string> fields) =>
        IngestDocumentAsync(handler, tenantId, new IngestedDocumentDto(sourceReference, documentType, sourceFields: fields));

    private static Task<ManagedDocumentBatchResultDto> IngestDocumentAsync(
        IngestManagedDocumentBatchHandler handler,
        string tenantId,
        IngestedDocumentDto document) =>
        handler.Handle(
            new IngestManagedDocumentBatchCommand { TenantId = tenantId, Documents = new[] { document } },
            CancellationToken.None);

    private static async Task SeedEntityTypeAsync(IConnectionFactory factory, string code, string identityKey)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_catalog.entity_types (code, label, identity_key, is_confidential, is_active)
            VALUES (@Code, @Label, @IdentityKey, false, true)
            """,
            new { Code = code, Label = code, IdentityKey = identityKey });
    }

    private static async Task SeedGraphProfileAsync(IConnectionFactory factory, string documentType)
    {
        var repository = new GedMappingProfileRepository(factory);
        var profile = GedMappingProfile.Create(
            documentType,
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: "WormPlusIndex",
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule("d", "$.fields.d", IsRequired: true, IsMulti: false) },
            entityRules: new[] { new EntityMappingRule("partner", "$.entities[?type=='p'].externalId", "$.entities[?type=='p'].display") },
            relationRules: new[] { new RelationMappingRule("concerne", "partner", "$.fields.rel") },
            createdAt: DateTimeOffset.UnixEpoch);

        await repository.InsertProfileAsync(profile, GedMappingChangeLogFactory.ForCreateProfile(profile, "ec@example.test", "Expert"));
    }

    private static async Task<IntegrationEvent<ManagedDocumentReceivedV1>> DrainSingleGedEventAsync(IConnectionFactory systemFactory)
    {
        using var connection = await systemFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<OutboxRow>(
            """
            SELECT id AS Id, event_type AS EventType, payload AS Payload, correlation_id AS CorrelationId,
                   module_source AS ModuleSource, version AS Version, occurred_at AS OccurredAt
            FROM outbox.pending_events
            WHERE event_type = @Type
            """,
            new { Type = GedEventTypes.ManagedDocumentReceived });

        var payload = JsonSerializer.Deserialize<ManagedDocumentReceivedV1>(row.Payload, OutboxJsonOptions)!;
        return new IntegrationEvent<ManagedDocumentReceivedV1>
        {
            EventId = row.Id,
            EventType = row.EventType,
            OccurredAt = row.OccurredAt,
            CorrelationId = row.CorrelationId,
            ModuleSource = row.ModuleSource,
            Version = row.Version,
            Payload = payload,
        };
    }

    private static async Task SeedAxisAsync(IConnectionFactory factory, string code, string dataType)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_catalog.axis_definitions (code, label, data_type, is_multi_value, is_active)
            VALUES (@Code, @Label, @DataType, false, true)
            """,
            new { Code = code, Label = code, DataType = dataType });
    }

    private static async Task SeedValidatedProfileAsync(IConnectionFactory factory, string documentType, string axisCode, string selector)
    {
        var repository = new GedMappingProfileRepository(factory);
        var profile = GedMappingProfile.Create(
            documentType,
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: "WormPlusIndex",
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule(axisCode, selector, IsRequired: true, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);

        await repository.InsertProfileAsync(profile, GedMappingChangeLogFactory.ForCreateProfile(profile, "ec@example.test", "Expert"));
    }

    private static async Task<long> CountRegistryAsync(IConnectionFactory factory, string tenantId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_ingestion.ged_received_documents WHERE tenant_id = @TenantId",
            new { TenantId = tenantId });
    }

    private static async Task<long> CountGedEventsAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM outbox.pending_events WHERE event_type = @Type",
            new { Type = GedEventTypes.ManagedDocumentReceived });
    }

    private static async Task<long> CountManagedDocumentsAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.managed_documents");
    }

    private static async Task<long> CountAxisLinksAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.document_axis_links");
    }

    private static async Task<long> CountEntityInstancesAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.entity_instances");
    }

    private static async Task<long> CountDocumentEntityLinksAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.document_entity_links");
    }

    private static async Task<long> CountEntityCreatedLogAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_index.entity_instance_change_log WHERE change_type = 'entity_created'");
    }

    private static async Task<string?> StatusAsync(IConnectionFactory factory, Guid managedDocumentId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT status FROM ged_index.managed_documents WHERE id = @Id",
            new { Id = managedDocumentId });
    }

    private static async Task<string?> DeferReasonAsync(IConnectionFactory factory, Guid managedDocumentId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT defer_reason FROM ged_index.managed_documents WHERE id = @Id",
            new { Id = managedDocumentId });
    }

    private static async Task<string?> AxisValueStringAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>("SELECT value_string FROM ged_index.document_axis_links LIMIT 1");
    }

    private sealed class OutboxRow
    {
        public Guid Id { get; init; }

        public string EventType { get; init; } = string.Empty;

        public string Payload { get; init; } = string.Empty;

        public Guid CorrelationId { get; init; }

        public string ModuleSource { get; init; } = string.Empty;

        public int Version { get; init; }

        public DateTimeOffset OccurredAt { get; init; }
    }
}
