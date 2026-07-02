namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Contracts.Queries;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration du port de lecture de la fiche document GED (GED09b, F19 §6.7) sur PostgreSQL réelle
/// (base par tenant). Prouvent : restitution méta + axes + entités courants ; masquage de confidentialité
/// MATÉRIALISÉ server-side (axe ET type d'entité confidentiels exclus sans le droit, révélés avec, anti-oracle) ;
/// valeur d'axe number TYPÉE en <c>decimal</c> (jamais double) ; not-found ; isolation cross-tenant (≥ 2 bases).
/// </summary>
[Collection("GedIntegration")]
public sealed class GedDocumentQueriesIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public GedDocumentQueriesIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Returns_document_metadata_axes_and_entities()
    {
        var factory = _fixture.CreateTenantDatabase();
        var queries = new PostgresGedDocumentQueries(factory);

        var docId = await InsertManagedDocumentAsync(factory, "Bordereau acheteur 42", docKind: "bordereau",
            archivePath: "_ged/bordereau/2026/06/K-42/manifest.json", contentHash: "abc123");
        var axis = await InsertAxisAsync(factory, "numero_lot", "string");
        await InsertStringAxisLinkAsync(factory, docId, axis, "LOT-42");
        var etype = await InsertEntityTypeAsync(factory, "acheteur", isConfidential: false);
        var entity = await InsertEntityAsync(factory, etype, "MARTIN SARL", identity: "12345678900011");
        await InsertDocEntityLinkAsync(factory, docId, entity, "acheteur");

        var view = await queries.GetAsync(docId, hasConfidentialRight: false);

        view.Should().NotBeNull();
        view!.Title.Should().Be("Bordereau acheteur 42");
        view.DocKind.Should().Be("bordereau");
        view.ArchivePath.Should().Be("_ged/bordereau/2026/06/K-42/manifest.json");
        view.ContentHash.Should().Be("abc123");
        view.Axes.Should().ContainSingle(a => a.Code == "numero_lot" && a.ValueString == "LOT-42");
        view.Entities.Should().ContainSingle(e => e.Role == "acheteur" && e.DisplayName == "MARTIN SARL" && e.IdentityValue == "12345678900011");
    }

    [Fact]
    public async Task Masks_confidential_axis_and_entity_without_right_and_reveals_with_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var queries = new PostgresGedDocumentQueries(factory);

        var docId = await InsertManagedDocumentAsync(factory, "doc");
        var pub = await InsertAxisAsync(factory, "axe_public", "string");
        var secret = await InsertAxisAsync(factory, "axe_secret", "string", isConfidential: true);
        await InsertStringAxisLinkAsync(factory, docId, pub, "pubval");
        await InsertStringAxisLinkAsync(factory, docId, secret, "secval");

        var etPublic = await InsertEntityTypeAsync(factory, "type_public", isConfidential: false);
        var etSecret = await InsertEntityTypeAsync(factory, "type_secret", isConfidential: true);
        var ePublic = await InsertEntityAsync(factory, etPublic, "Public SA");
        var eSecret = await InsertEntityAsync(factory, etSecret, "Confidentiel SA");
        await InsertDocEntityLinkAsync(factory, docId, ePublic, "site");
        await InsertDocEntityLinkAsync(factory, docId, eSecret, "beneficiaire");

        var withoutRight = await queries.GetAsync(docId, hasConfidentialRight: false);
        withoutRight!.Axes.Select(a => a.Code).Should().Contain("axe_public").And.NotContain("axe_secret");
        withoutRight.Entities.Select(e => e.EntityTypeCode).Should().Contain("type_public").And.NotContain("type_secret");

        var withRight = await queries.GetAsync(docId, hasConfidentialRight: true);
        withRight!.Axes.Select(a => a.Code).Should().Contain("axe_public").And.Contain("axe_secret");
        withRight.Entities.Select(e => e.EntityTypeCode).Should().Contain("type_public").And.Contain("type_secret");
    }

    [Fact]
    public async Task Returns_number_axis_value_as_exact_decimal_with_scale()
    {
        var factory = _fixture.CreateTenantDatabase();
        var queries = new PostgresGedDocumentQueries(factory);

        var docId = await InsertManagedDocumentAsync(factory, "situation travaux");
        var axis = await InsertAxisAsync(factory, "montant_ht_cumule", "number", valueScale: 2, unit: "EUR");
        await InsertNumberAxisLinkAsync(factory, docId, axis, 1234.56m, "1234.56");

        var view = await queries.GetAsync(docId, hasConfidentialRight: false);

        var value = view!.Axes.Should().ContainSingle(a => a.Code == "montant_ht_cumule").Subject;
        value.DataType.Should().Be("number");
        value.ValueNumber.Should().Be(1234.56m);
        value.ValueScale.Should().Be(2);
        value.Unit.Should().Be("EUR");
    }

    [Fact]
    public async Task Returns_null_when_the_document_does_not_exist()
    {
        var factory = _fixture.CreateTenantDatabase();
        var queries = new PostgresGedDocumentQueries(factory);

        var view = await queries.GetAsync(Guid.NewGuid(), hasConfidentialRight: true);

        view.Should().BeNull();
    }

    [Fact]
    public async Task Is_scoped_to_the_tenant_database()
    {
        var tenantA = _fixture.CreateTenantDatabase();
        var tenantB = _fixture.CreateTenantDatabase();
        var docId = await InsertManagedDocumentAsync(tenantA, "doc tenant A");

        (await new PostgresGedDocumentQueries(tenantA).GetAsync(docId, hasConfidentialRight: true)).Should().NotBeNull();
        (await new PostgresGedDocumentQueries(tenantB).GetAsync(docId, hasConfidentialRight: true))
            .Should().BeNull("un document du tenant A est invisible depuis la base du tenant B");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static async Task<Guid> InsertManagedDocumentAsync(
        IConnectionFactory factory,
        string title,
        string status = "indexed",
        string? docKind = null,
        string? archivePath = null,
        string? contentHash = null)
    {
        var id = Guid.NewGuid();
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.managed_documents (id, title, doc_kind, status, archive_path, content_hash)
            VALUES (@Id, @Title, @DocKind, @Status, @ArchivePath, @ContentHash)
            """,
            new { Id = id, Title = title, DocKind = docKind, Status = status, ArchivePath = archivePath, ContentHash = contentHash });
        return id;
    }

    private static async Task<Guid> InsertAxisAsync(
        IConnectionFactory factory,
        string code,
        string dataType,
        bool isConfidential = false,
        int? valueScale = null,
        string? unit = null)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ged_catalog.axis_definitions
                (code, label, data_type, value_scale, unit, is_confidential, is_active)
            VALUES (@Code, @Code, @DataType, @Scale, @Unit, @Confidential, true)
            RETURNING id
            """,
            new { Code = code, DataType = dataType, Scale = valueScale, Unit = unit, Confidential = isConfidential });
    }

    private static async Task InsertStringAxisLinkAsync(IConnectionFactory factory, Guid docId, Guid axisId, string value)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, value_string, normalized_value, source)
            VALUES (@Doc, @Axis, @Value, @Normalized, 'manual')
            """,
            new { Doc = docId, Axis = axisId, Value = value, Normalized = value.ToLowerInvariant() });
    }

    private static async Task InsertNumberAxisLinkAsync(IConnectionFactory factory, Guid docId, Guid axisId, decimal value, string normalized)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, value_number, normalized_value, source)
            VALUES (@Doc, @Axis, @Value, @Normalized, 'manual')
            """,
            new { Doc = docId, Axis = axisId, Value = value, Normalized = normalized });
    }

    private static async Task<Guid> InsertEntityTypeAsync(IConnectionFactory factory, string code, bool isConfidential)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ged_catalog.entity_types (code, label, is_confidential, is_active)
            VALUES (@Code, @Code, @Confidential, true)
            RETURNING id
            """,
            new { Code = code, Confidential = isConfidential });
    }

    private static async Task<Guid> InsertEntityAsync(IConnectionFactory factory, Guid entityTypeId, string displayName, string? identity = null)
    {
        var id = Guid.NewGuid();
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_instances (id, entity_type_id, display_name, identity_value) VALUES (@Id, @Type, @Name, @Identity)",
            new { Id = id, Type = entityTypeId, Name = displayName, Identity = identity });
        return id;
    }

    private static async Task InsertDocEntityLinkAsync(IConnectionFactory factory, Guid docId, Guid entityId, string role)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.document_entity_links (managed_document_id, entity_id, role, relation_type, source)
            VALUES (@Doc, @Entity, @Role, 'direct', 'manual')
            """,
            new { Doc = docId, Entity = entityId, Role = role });
    }
}
