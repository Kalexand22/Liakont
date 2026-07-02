namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation PostgreSQL du port de lecture de la fiche document GED (F19 §6.7, GED09b), sur la base DU
/// TENANT (isolation = la connexion, INV-GED-08). Trois lectures : (1) les méta-colonnes de
/// <c>managed_documents</c> (§3.4.1) ; (2) les valeurs d'axes COURANTES (vue <c>current_axis_links</c>, exclut
/// rétractées/superséedées) jointes au catalogue d'axes ; (3) les liens d'entités COURANTS (vue
/// <c>current_document_entity_links</c>) joints aux instances + types d'entité. Le prédicat de confidentialité
/// est MATÉRIALISÉ dans le SQL (RL-31, anti-oracle) : un axe/type d'entité <c>is_confidential</c> est EXCLU
/// lorsque l'acteur n'a pas le droit (<c>@HasRight</c> = <c>false</c>). Colonnes <c>snake_case</c> aliasées
/// (Dapper n'active pas <c>MatchNamesWithUnderscores</c> ici).
/// </summary>
internal sealed class PostgresGedDocumentQueries : IGedDocumentQueries
{
    private const string DocumentSql = """
        SELECT md.id                 AS Id,
               md.title              AS Title,
               md.doc_kind           AS DocKind,
               md.fiscal_document_id AS FiscalDocumentId,
               md.archive_entry_id   AS ArchiveEntryId,
               md.archive_path       AS ArchivePath,
               md.content_hash       AS ContentHash,
               md.status             AS Status,
               md.retention_class    AS RetentionClass,
               md.defer_reason       AS DeferReason,
               md.created_utc        AS CreatedUtc,
               md.updated_utc        AS UpdatedUtc
        FROM ged_index.managed_documents md
        WHERE md.id = @Id;
        """;

    // Confidentialité MATÉRIALISÉE (RL-31) : un axe confidentiel sans le droit ne remonte jamais (anti-oracle).
    // value_date rendu en texte ISO pour éviter toute ambiguïté de mapping date → CLR ; entité liée résolue par
    // LEFT JOIN (axe data_type='entity').
    private const string AxesSql = """
        SELECT ad.code                                       AS Code,
               ad.label                                      AS Label,
               ad.data_type                                  AS DataType,
               ad.unit                                       AS Unit,
               ad.value_scale                                AS ValueScale,
               dal.value_string                              AS ValueString,
               dal.value_number                              AS ValueNumber,
               to_char(dal.value_date, 'YYYY-MM-DD')         AS ValueDate,
               dal.value_boolean                             AS ValueBoolean,
               ei.display_name                               AS ValueEntityName,
               dal.normalized_value                          AS NormalizedValue
        FROM ged_index.current_axis_links dal
        JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
        LEFT JOIN ged_index.entity_instances ei ON ei.id = dal.value_entity_id
        WHERE dal.managed_document_id = @Id
          AND (ad.is_confidential = false OR @HasRight)
        ORDER BY ad.ordinal, ad.code, dal.seq;
        """;

    // Confidentialité héritée du TYPE d'entité (§6.5) : un type confidentiel sans le droit est exclu (anti-oracle).
    private const string EntitiesSql = """
        SELECT del.role          AS Role,
               et.code           AS EntityTypeCode,
               et.label          AS EntityTypeLabel,
               ei.display_name   AS DisplayName,
               ei.identity_value AS IdentityValue
        FROM ged_index.current_document_entity_links del
        JOIN ged_index.entity_instances ei ON ei.id = del.entity_id
        JOIN ged_catalog.entity_types et ON et.id = ei.entity_type_id
        WHERE del.managed_document_id = @Id
          AND (et.is_confidential = false OR @HasRight)
        ORDER BY del.role, ei.display_name, del.seq;
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresGedDocumentQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<GedManagedDocumentView?> GetAsync(
        Guid managedDocumentId,
        bool hasConfidentialRight,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        DocumentRow? document = await connection.QuerySingleOrDefaultAsync<DocumentRow>(new CommandDefinition(
            DocumentSql, new { Id = managedDocumentId }, cancellationToken: cancellationToken));
        if (document is null)
        {
            return null;
        }

        var axisParameters = new { Id = managedDocumentId, HasRight = hasConfidentialRight };

        var axes = (await connection.QueryAsync<AxisRow>(new CommandDefinition(
            AxesSql, axisParameters, cancellationToken: cancellationToken))).ToList();

        var entities = (await connection.QueryAsync<EntityRow>(new CommandDefinition(
            EntitiesSql, axisParameters, cancellationToken: cancellationToken))).ToList();

        return new GedManagedDocumentView
        {
            Id = document.Id,
            Title = document.Title,
            DocKind = document.DocKind,
            Status = document.Status,
            RetentionClass = document.RetentionClass,
            DeferReason = document.DeferReason,
            FiscalDocumentId = document.FiscalDocumentId,
            ArchiveEntryId = document.ArchiveEntryId,
            ArchivePath = document.ArchivePath,
            ContentHash = document.ContentHash,
            CreatedUtc = document.CreatedUtc,
            UpdatedUtc = document.UpdatedUtc,
            Axes = axes.Select(a => new GedManagedAxisValue
            {
                Code = a.Code,
                Label = a.Label,
                DataType = a.DataType,
                Unit = a.Unit,
                ValueScale = a.ValueScale,
                ValueString = a.ValueString,
                ValueNumber = a.ValueNumber,
                ValueDate = a.ValueDate,
                ValueBoolean = a.ValueBoolean,
                ValueEntityName = a.ValueEntityName,
                NormalizedValue = a.NormalizedValue,
            }).ToList(),
            Entities = entities.Select(e => new GedManagedEntityLink(
                e.Role, e.EntityTypeCode, e.EntityTypeLabel, e.DisplayName, e.IdentityValue)).ToList(),
        };
    }

    private sealed class DocumentRow
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string? DocKind { get; init; }

        public Guid? FiscalDocumentId { get; init; }

        public Guid? ArchiveEntryId { get; init; }

        public string? ArchivePath { get; init; }

        public string? ContentHash { get; init; }

        public string Status { get; init; } = string.Empty;

        public string RetentionClass { get; init; } = string.Empty;

        public string? DeferReason { get; init; }

        public DateTimeOffset CreatedUtc { get; init; }

        public DateTimeOffset? UpdatedUtc { get; init; }
    }

    private sealed class AxisRow
    {
        public string Code { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public string DataType { get; init; } = string.Empty;

        public string? Unit { get; init; }

        public int? ValueScale { get; init; }

        public string? ValueString { get; init; }

        public decimal? ValueNumber { get; init; }

        public string? ValueDate { get; init; }

        public bool? ValueBoolean { get; init; }

        public string? ValueEntityName { get; init; }

        public string? NormalizedValue { get; init; }
    }

    private sealed class EntityRow
    {
        public string Role { get; init; } = string.Empty;

        public string EntityTypeCode { get; init; } = string.Empty;

        public string EntityTypeLabel { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string? IdentityValue { get; init; }
    }
}
