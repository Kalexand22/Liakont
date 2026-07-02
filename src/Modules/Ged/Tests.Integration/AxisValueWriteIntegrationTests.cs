namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Index;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration base RÉELLE (Testcontainers PostgreSQL) de l'écriture de valeurs d'axe GED (GED04,
/// F19 §3.7/§3.4.3). Prouvent l'invariant mono-valeur SOUS CONCURRENCE (INV-GED-03 / RL-02 — un test séquentiel
/// serait un faux-vert), la supersession chaînée append-only, la cardinalité multi, le rangement <c>decimal</c>
/// exact, les refus (axe inconnu, enum hors vocabulaire) et le tenant-scoping par la connexion (n°9, ≥ 2 bases).
/// </summary>
[Collection("GedIntegration")]
public sealed class AxisValueWriteIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public AxisValueWriteIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Concurrent_writes_on_a_mono_axis_yield_a_single_current_value()
    {
        var factory = _fixture.CreateTenantDatabase();
        var axisId = await SeedAxisAsync(factory, "chantier", "string", isMultiValue: false);
        var documentId = await SeedManagedDocumentAsync(factory);
        var unitOfWorkFactory = new PostgresGedIndexUnitOfWorkFactory(factory);

        const int writers = 8;
        var tasks = Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
        {
            await using var unitOfWork = await unitOfWorkFactory.BeginAsync();
            var link = new DocumentAxisLink(documentId, axisId, StringValue($"v{i}"), "agent");
            await unitOfWork.AppendAxisLinkAsync(link, isSingleValued: true);
            await unitOfWork.CommitAsync();
        }));

        await Task.WhenAll(tasks);

        (await CurrentCountAsync(factory, documentId, axisId)).Should().Be(
            1,
            "la garde de concurrence RL-02 garantit UNE seule valeur courante pour un axe mono, même sous appends simultanés");
        (await TotalCountAsync(factory, documentId, axisId)).Should().Be(
            writers,
            "append pur : toutes les écritures sont conservées, une seule reste courante (les autres superséedées)");
    }

    [Fact]
    public async Task A_new_mono_value_supersedes_the_previous_current_value()
    {
        var factory = _fixture.CreateTenantDatabase();
        var axisId = await SeedAxisAsync(factory, "statut", "string", isMultiValue: false);
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        await handler.Handle(SetCommand(documentId, "statut", "brouillon"), CancellationToken.None);
        await handler.Handle(SetCommand(documentId, "statut", "valide"), CancellationToken.None);

        (await CurrentCountAsync(factory, documentId, axisId)).Should().Be(1);
        (await CurrentValueStringAsync(factory, documentId, axisId)).Should().Be("valide");
        (await TotalCountAsync(factory, documentId, axisId)).Should().Be(2, "l'ancienne valeur reste en base (append pur)");
    }

    [Fact]
    public async Task A_multi_value_axis_keeps_every_appended_value_current()
    {
        var factory = _fixture.CreateTenantDatabase();
        var axisId = await SeedAxisAsync(factory, "tag", "string", isMultiValue: true);
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        await handler.Handle(SetCommand(documentId, "tag", "urgent"), CancellationToken.None);
        await handler.Handle(SetCommand(documentId, "tag", "litige"), CancellationToken.None);

        (await CurrentCountAsync(factory, documentId, axisId)).Should().Be(2, "un axe multi ne supersède pas la valeur courante");
    }

    [Fact]
    public async Task A_number_value_is_stored_as_exact_decimal_half_up_in_the_typed_column()
    {
        var factory = _fixture.CreateTenantDatabase();
        await SeedAxisAsync(factory, "montant_ht", "number", isMultiValue: false, valueScale: 2);
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        var id = await handler.Handle(SetCommand(documentId, "montant_ht", "1234.505"), CancellationToken.None);

        using var connection = await factory.OpenAsync();
        var stored = await connection.QueryFirstAsync<StoredAxisValue>(
            """
            SELECT value_number AS ValueNumber, value_string AS ValueString, normalized_value AS NormalizedValue
            FROM ged_index.document_axis_links
            WHERE id = @Id
            """,
            new { Id = id });

        stored.ValueNumber.Should().Be(1234.51m, "arrondi commercial half-up à l'échelle de l'axe (jamais double/float)");
        stored.NormalizedValue.Should().Be("1234.51");
        stored.ValueString.Should().BeNull("une seule colonne typée est renseignée (anti-EAV)");
    }

    [Fact]
    public async Task An_unknown_axis_is_refused_and_writes_nothing()
    {
        var factory = _fixture.CreateTenantDatabase();
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        var act = () => handler.Handle(SetCommand(documentId, "axe_inconnu", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<AxisNotResolvableException>();
        using var connection = await factory.OpenAsync();
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.document_axis_links"))
            .Should().Be(0, "un axe refusé n'écrit aucun lien (jamais deviner, règle 2)");
    }

    [Fact]
    public async Task An_enum_value_outside_the_declared_vocabulary_is_refused()
    {
        var factory = _fixture.CreateTenantDatabase();
        var axisId = await SeedAxisAsync(factory, "langue", "enum", isMultiValue: false);
        await SeedEnumValueAsync(factory, axisId, "fr");
        await SeedEnumValueAsync(factory, axisId, "de");
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        var refused = () => handler.Handle(SetCommand(documentId, "langue", "es"), CancellationToken.None);
        await refused.Should().ThrowAsync<AxisValueFormatException>();

        await handler.Handle(SetCommand(documentId, "langue", "fr"), CancellationToken.None);
        (await CurrentValueStringAsync(factory, documentId, axisId)).Should().Be("fr", "un code du vocabulaire déclaré est accepté");
    }

    [Fact]
    public async Task Axis_values_are_scoped_to_the_writing_tenant_database()
    {
        var tenantA = _fixture.CreateTenantDatabase();
        var tenantB = _fixture.CreateTenantDatabase();
        var axisA = await SeedAxisAsync(tenantA, "chantier", "string", isMultiValue: false);
        await SeedAxisAsync(tenantB, "chantier", "string", isMultiValue: false);
        var documentA = await SeedManagedDocumentAsync(tenantA);

        await HandlerFor(tenantA).Handle(SetCommand(documentA, "chantier", "A1"), CancellationToken.None);

        (await CurrentCountAsync(tenantA, documentA, axisA)).Should().Be(1, "la valeur est écrite dans la base du tenant A");
        using var connectionB = await tenantB.OpenAsync();
        (await connectionB.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.document_axis_links"))
            .Should().Be(0, "aucune donnée n'a fui vers la base du tenant B (tenant-scopé par la connexion, n°9)");
    }

    [Fact]
    public async Task Setting_a_searchable_axis_reprojects_full_text_so_the_correction_is_found_and_the_old_value_is_gone()
    {
        var factory = _fixture.CreateTenantDatabase();
        await SeedAxisAsync(factory, "statut", "string", isMultiValue: false, isSearchable: true);
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);
        var index = new PostgresDocumentSearchIndex(factory, new PostgresAxisCatalog(factory));

        await handler.Handle(SetCommand(documentId, "statut", "valeurobsolete"), CancellationToken.None);
        (await index.SearchAsync(new DocumentSearchQuery { FullText = "valeurobsolete" })).Hits
            .Should().ContainSingle(h => h.ManagedDocumentId == documentId,
                "la 1re écriture searchable est projetée dans le search_vector");

        // Correction opérateur (« valeurobsolete » → « valeurcorrigee ») : l'axe mono supersède l'ancienne valeur.
        await handler.Handle(SetCommand(documentId, "statut", "valeurcorrigee"), CancellationToken.None);

        (await index.SearchAsync(new DocumentSearchQuery { FullText = "valeurcorrigee" })).Hits
            .Should().ContainSingle(h => h.ManagedDocumentId == documentId,
                "la correction est retrouvée en plein-texte : l'écriture manuelle a re-projeté le dérivé (GED07)");
        (await index.SearchAsync(new DocumentSearchQuery { FullText = "valeurobsolete" })).Hits
            .Should().BeEmpty("l'ancienne valeur supersédée ne remonte plus le document (le search_vector n'est plus figé sur l'ingestion)");
    }

    [Fact]
    public async Task Setting_a_non_searchable_axis_does_not_reproject_the_derived_search_index()
    {
        var factory = _fixture.CreateTenantDatabase();
        await SeedAxisAsync(factory, "note_interne", "string", isMultiValue: false, isSearchable: false);
        var documentId = await SeedManagedDocumentAsync(factory);
        var handler = HandlerFor(factory);

        await handler.Handle(SetCommand(documentId, "note_interne", "valeur"), CancellationToken.None);

        using var connection = await factory.OpenAsync();
        (await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_index.document_search WHERE managed_document_id = @Doc",
            new { Doc = documentId }))
            .Should().Be(0, "un axe non searchable n'alimente pas le search_vector : aucune re-projection inutile (GED07)");
    }

    private static SetAxisValueCommandHandler HandlerFor(IConnectionFactory factory) =>
        new(new PostgresAxisCatalog(factory),
            new PostgresGedIndexUnitOfWorkFactory(factory),
            new PostgresDocumentSearchIndex(factory, new PostgresAxisCatalog(factory)));

    private static NormalizedAxisValue StringValue(string rawValue) =>
        ValueNormalizer.Normalize(AxisDataType.Text, valueScale: null, rawValue);

    private static SetAxisValueCommand SetCommand(Guid documentId, string axisCode, string rawValue) =>
        new()
        {
            DocumentId = documentId,
            AxisCode = axisCode,
            RawValue = rawValue,
            Source = "agent",
        };

    private static async Task<Guid> SeedAxisAsync(
        IConnectionFactory factory,
        string code,
        string dataType,
        bool isMultiValue,
        int? valueScale = null,
        bool isSearchable = true)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ged_catalog.axis_definitions (code, label, data_type, value_scale, is_multi_value, is_searchable, is_active)
            VALUES (@Code, @Label, @DataType, @ValueScale, @IsMultiValue, @IsSearchable, true)
            RETURNING id
            """,
            new { Code = code, Label = code, DataType = dataType, ValueScale = valueScale, IsMultiValue = isMultiValue, IsSearchable = isSearchable });
    }

    private static async Task SeedEnumValueAsync(IConnectionFactory factory, Guid axisId, string code)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_values (axis_id, code, label) VALUES (@AxisId, @Code, @Code)",
            new { AxisId = axisId, Code = code });
    }

    private static async Task<Guid> SeedManagedDocumentAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.managed_documents (id, title, status) VALUES (@Id, 'document GED de test', 'draft')",
            new { Id = id });
        return id;
    }

    private static async Task<long> CurrentCountAsync(IConnectionFactory factory, Guid documentId, Guid axisId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_index.current_axis_links WHERE managed_document_id = @DocumentId AND axis_id = @AxisId",
            new { DocumentId = documentId, AxisId = axisId });
    }

    private static async Task<long> TotalCountAsync(IConnectionFactory factory, Guid documentId, Guid axisId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_index.document_axis_links WHERE managed_document_id = @DocumentId AND axis_id = @AxisId",
            new { DocumentId = documentId, AxisId = axisId });
    }

    private static async Task<string?> CurrentValueStringAsync(IConnectionFactory factory, Guid documentId, Guid axisId)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT value_string FROM ged_index.current_axis_links WHERE managed_document_id = @DocumentId AND axis_id = @AxisId",
            new { DocumentId = documentId, AxisId = axisId });
    }

    private sealed class StoredAxisValue
    {
        public decimal? ValueNumber { get; set; }

        public string? ValueString { get; set; }

        public string? NormalizedValue { get; set; }
    }
}
