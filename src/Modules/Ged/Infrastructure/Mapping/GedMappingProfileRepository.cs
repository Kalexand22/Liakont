namespace Liakont.Modules.Ged.Infrastructure.Mapping;

using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application.Mapping;
using Liakont.Modules.Ged.Domain.Mapping;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Repository Dapper des profils de mapping GED (F19 §4.5), tenant-scopé par la connexion (règle 9 ; la base EST
/// le tenant, aucune colonne <c>tenant_id</c> — F19 §3.2). Implémente la surface de LECTURE
/// <see cref="IGedMappingProfileStore"/> (charge le profil VALIDÉ, jamais un profil non validé) et expose
/// l'écriture atomique profil + entrée de journal append-only (consommée par les tests et le futur handler de
/// paramétrage de profil ; le consommateur d'ingestion GED05b lit via <see cref="IGedMappingProfileStore"/>).
/// </summary>
internal sealed class GedMappingProfileRepository : IGedMappingProfileStore
{
    private const string InsertProfileSql = """
        INSERT INTO ged_catalog.ged_mapping_profiles
            (id, document_type, profile_version, storage_policy, validated_by, validated_date,
             axis_rules, entity_rules, relation_rules, created_at, updated_at)
        VALUES
            (@Id, @DocumentType, @ProfileVersion, @StoragePolicy, @ValidatedBy, @ValidatedDate,
             @AxisRules::jsonb, @EntityRules::jsonb, @RelationRules::jsonb, @CreatedAt, @UpdatedAt)
        """;

    private const string InsertChangeLogSql = """
        INSERT INTO ged_catalog.ged_mapping_change_log
            (change_type, profile_id, document_type, profile_version, before_value, after_value,
             operator_identity, operator_name)
        VALUES
            (@ChangeType, @ProfileId, @DocumentType, @ProfileVersion, @BeforeJson::jsonb, @AfterJson::jsonb,
             @OperatorIdentity, @OperatorName)
        """;

    private const string SelectValidatedProfileSql = """
        SELECT id                AS "Id",
               document_type      AS "DocumentType",
               profile_version    AS "ProfileVersion",
               storage_policy     AS "StoragePolicy",
               validated_by       AS "ValidatedBy",
               validated_date     AS "ValidatedDate",
               axis_rules         AS "AxisRules",
               entity_rules       AS "EntityRules",
               relation_rules     AS "RelationRules",
               created_at         AS "CreatedAt",
               updated_at         AS "UpdatedAt"
        FROM ged_catalog.ged_mapping_profiles
        WHERE document_type = @DocumentType AND validated_by IS NOT NULL
        """;

    private readonly IConnectionFactory _connectionFactory;

    /// <summary>Initialise le repository avec la fabrique de connexions tenant.</summary>
    /// <param name="connectionFactory">La fabrique de connexions (base DU tenant).</param>
    public GedMappingProfileRepository(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Insère un profil ET son entrée de journal append-only dans la MÊME transaction (atomicité : un échec de
    /// l'un annule l'autre). Lève <see cref="ConflictException"/> si un profil validé existe déjà pour ce
    /// <c>documentType</c> (index unique partiel) ou en cas de doublon (documentType, version).
    /// </summary>
    /// <param name="profile">Le profil à insérer.</param>
    /// <param name="changeLog">L'entrée de journal (naissance/validation).</param>
    /// <param name="ct">Jeton d'annulation.</param>
    public async Task InsertProfileAsync(
        GedMappingProfile profile,
        GedMappingChangeLogEntry changeLog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(changeLog);

        await using var txn = await TransactionScope.BeginAsync(_connectionFactory, ct);

        try
        {
            await txn.Connection.ExecuteAsync(new CommandDefinition(
                InsertProfileSql,
                new
                {
                    profile.Id,
                    profile.DocumentType,
                    profile.ProfileVersion,
                    profile.StoragePolicy,
                    profile.ValidatedBy,
                    profile.ValidatedDate,
                    AxisRules = GedMappingProfileJson.Serialize(profile.AxisRules),
                    EntityRules = GedMappingProfileJson.Serialize(profile.EntityRules),
                    RelationRules = GedMappingProfileJson.Serialize(profile.RelationRules),
                    profile.CreatedAt,
                    profile.UpdatedAt,
                },
                txn.Transaction,
                cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(
                $"Un profil de mapping GED en conflit existe déjà pour le type de document « {profile.DocumentType} ».",
                ex);
        }

        await txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertChangeLogSql,
            new
            {
                changeLog.ChangeType,
                changeLog.ProfileId,
                changeLog.DocumentType,
                changeLog.ProfileVersion,
                changeLog.BeforeJson,
                changeLog.AfterJson,
                changeLog.OperatorIdentity,
                changeLog.OperatorName,
            },
            txn.Transaction,
            cancellationToken: ct));

        await txn.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<GedMappingProfile?> GetValidatedProfileAsync(
        string documentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return null;
        }

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
            SelectValidatedProfileSql,
            new { DocumentType = documentType },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return GedMappingProfile.Reconstitute(
            row.Id,
            row.DocumentType,
            row.ProfileVersion,
            row.StoragePolicy,
            row.ValidatedBy,
            row.ValidatedDate,
            GedMappingProfileJson.Deserialize<AxisMappingRule>(row.AxisRules),
            GedMappingProfileJson.Deserialize<EntityMappingRule>(row.EntityRules),
            GedMappingProfileJson.Deserialize<RelationMappingRule>(row.RelationRules),
            row.CreatedAt,
            row.UpdatedAt);
    }

    // Modèle de lecture : Dapper ne mappe pas snake_case (MatchNamesWithUnderscores jamais activé) ; les
    // colonnes sont aliasées en PascalCase dans le SELECT. Les colonnes jsonb sont lues en string.
    private sealed class ProfileRow
    {
        public Guid Id { get; init; }

        public string DocumentType { get; init; } = string.Empty;

        public string ProfileVersion { get; init; } = string.Empty;

        public string? StoragePolicy { get; init; }

        public string? ValidatedBy { get; init; }

        public DateOnly? ValidatedDate { get; init; }

        public string? AxisRules { get; init; }

        public string? EntityRules { get; init; }

        public string? RelationRules { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }
    }
}
