namespace Liakont.Modules.Ged.Infrastructure;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Domain.Index;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper de l'INDEX GED (base DU TENANT, schéma <c>ged_index</c>), ouverte via
/// <see cref="TransactionScope"/> — l'isolation EST la connexion (F19 §3.2). <see cref="AppendAxisLinkAsync"/>
/// écrit en APPEND PUR (jamais d'UPDATE, le trigger l'interdit) et, pour un axe MONO, prend une garde de
/// concurrence <c>pg_advisory_xact_lock</c> AVANT de superséder la valeur courante (RL-02). GED05b ajoute le
/// chemin du consommateur d'ingestion : garde de concurrence PAR DOCUMENT (<see cref="BeginDocumentIndexingAsync"/>),
/// UPSERT idempotent de <c>managed_documents</c>, résolution d'entité et lien document↔entité (append pur).
/// </summary>
internal sealed class PostgresGedIndexUnitOfWork : IGedIndexUnitOfWork
{
    // RL-02 / RL-04 — les gardes de supersession (axe MONO) et d'indexation (document) sont des read-modify-write
    // sous verrou consultatif : corrects seulement sous READ COMMITTED (le SELECT qui suit le verrou prend un
    // snapshot FRAIS voyant la ligne validée par l'écrivain précédent). Épinglé explicitement, UNE SEULE FOIS
    // (premier statement de la transaction) — TransactionScope (socle vendored) ouvre sans niveau explicite.
    private const string EnsureReadCommittedSql = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";

    private const string AdvisoryLockSql = "SELECT pg_advisory_xact_lock(hashtext(@LockKey))";

    // Verrou consultatif d'indexation d'un document (tenu jusqu'au commit) : deux livraisons simultanées du même
    // ManagedDocumentReceivedV1 sont sérialisées ; la seconde lit alors le statut terminal et no-ope (RL-04).
    private const string DocumentStatusSql = "SELECT status FROM ged_index.managed_documents WHERE id = @Id";

    // UPSERT de l'entité-pivot au statut FINAL (indexed/deferred) : ON CONFLICT (id) DO NOTHING = idempotence RL-04.
    private const string UpsertManagedDocumentSql = """
        INSERT INTO ged_index.managed_documents (id, title, doc_kind, status, retention_class, defer_reason)
        VALUES (@Id, @Title, @DocKind, @Status, @RetentionClass, @DeferReason)
        ON CONFLICT (id) DO NOTHING
        """;

    private const string CurrentAxisValueSql = """
        SELECT id
        FROM ged_index.current_axis_links
        WHERE managed_document_id = @ManagedDocumentId AND axis_id = @AxisId
        """;

    private const string InsertAxisLinkSql = """
        INSERT INTO ged_index.document_axis_links
            (managed_document_id, axis_id, value_string, value_number, value_date, value_boolean,
             value_entity_id, value_json, normalized_value, source, confidence_score, supersedes_id,
             operator_identity)
        VALUES
            (@ManagedDocumentId, @AxisId, @ValueString, @ValueNumber, @ValueDate, @ValueBoolean,
             @ValueEntityId, @ValueJson::jsonb, @NormalizedValue, @Source, @ConfidenceScore, @SupersedesId,
             @OperatorIdentity)
        RETURNING id
        """;

    // Résolution d'identité (§4.4) : réutilise l'index ix_ei_identity (entity_type_id, identity_value).
    private const string FindEntityByIdentitySql = """
        SELECT id
        FROM ged_index.entity_instances
        WHERE entity_type_id = @EntityTypeId AND identity_value = @IdentityValue
        LIMIT 1
        """;

    // Le search_vector est maintenu EN PLACE à la création (F19 §3.4.2, asymétrie assumée vs document_search) ;
    // config 'french' (D11), comme la recherche document (GED08).
    private const string InsertEntitySql = """
        INSERT INTO ged_index.entity_instances (entity_type_id, display_name, identity_value, search_vector, is_active)
        VALUES (@EntityTypeId, @DisplayName, @IdentityValue, to_tsvector('french', @DisplayName), true)
        RETURNING id
        """;

    // Piste d'audit append-only du graphe (V013) : la création d'une instance est tracée (entity_created).
    private const string InsertEntityCreatedLogSql = """
        INSERT INTO ged_index.entity_instance_change_log (change_type, entity_instance_id, after_value)
        VALUES ('entity_created', @EntityInstanceId, @AfterValue::jsonb)
        """;

    private const string InsertDocumentEntityLinkSql = """
        INSERT INTO ged_index.document_entity_links
            (managed_document_id, entity_id, role, relation_type, confidence_score, source, operator_identity)
        VALUES
            (@ManagedDocumentId, @EntityId, @Role, @RelationType, @ConfidenceScore, @Source, @OperatorIdentity)
        RETURNING id
        """;

    private readonly TransactionScope _txn;

    // L'épinglage READ COMMITTED doit être le PREMIER statement de la transaction (SET TRANSACTION ISOLATION LEVEL
    // échoue après toute requête). Ce drapeau garantit qu'il n'est émis qu'une fois, que l'appelant passe par
    // BeginDocumentIndexingAsync (consommateur) ou directement par AppendAxisLinkAsync (SetAxisValueCommandHandler).
    private bool _isolationPinned;

    private PostgresGedIndexUnitOfWork(TransactionScope txn) => _txn = txn;

    public static async Task<PostgresGedIndexUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, cancellationToken);
        return new PostgresGedIndexUnitOfWork(txn);
    }

    public async Task<string?> BeginDocumentIndexingAsync(Guid managedDocumentId, CancellationToken cancellationToken = default)
    {
        await EnsureReadCommittedPinnedAsync(cancellationToken);

        var lockKey = FormattableString.Invariant($"doc:{managedDocumentId:D}");
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            AdvisoryLockSql,
            new { LockKey = lockKey },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        return await _txn.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            DocumentStatusSql,
            new { Id = managedDocumentId },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task UpsertManagedDocumentAsync(ManagedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            UpsertManagedDocumentSql,
            new
            {
                document.Id,
                document.Title,
                document.DocKind,
                document.Status,
                document.RetentionClass,
                document.DeferReason,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<Guid> AppendAxisLinkAsync(DocumentAxisLink link, bool isSingleValued, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        Guid? supersedesId = null;

        if (isSingleValued)
        {
            await EnsureReadCommittedPinnedAsync(cancellationToken);

            var lockKey = FormattableString.Invariant($"{link.ManagedDocumentId:D}:{link.AxisId:D}");

            // RL-02 — supersession sous garde : verrou → lire la courante → superséder (correct sous READ COMMITTED,
            // épinglé par EnsureReadCommittedPinnedAsync). Le verrou (clé document+axe) est distinct du verrou
            // d'indexation (clé « doc:… ») : deux espaces de clé, aucune collision.
            await _txn.Connection.ExecuteAsync(new CommandDefinition(
                AdvisoryLockSql,
                new { LockKey = lockKey },
                _txn.Transaction,
                cancellationToken: cancellationToken));

            supersedesId = await _txn.Connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                CurrentAxisValueSql,
                new { link.ManagedDocumentId, link.AxisId },
                _txn.Transaction,
                cancellationToken: cancellationToken));
        }

        var value = link.Value;

        return await _txn.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertAxisLinkSql,
            new
            {
                link.ManagedDocumentId,
                link.AxisId,
                value.ValueString,
                value.ValueNumber,
                value.ValueDate,
                value.ValueBoolean,
                value.ValueEntityId,
                value.ValueJson,
                value.NormalizedValue,
                link.Source,
                link.ConfidenceScore,
                SupersedesId = supersedesId,
                link.OperatorIdentity,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<Guid> ResolveOrCreateEntityAsync(
        Guid entityTypeId,
        string? identityValue,
        string displayName,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        // §4.4 — clé d'identité présente : réutiliser l'instance existante (déduplication idempotente). Absente :
        // pas de déduplication auto, création par observation (jamais deviner une fusion, règle 2).
        if (identityValue is not null)
        {
            var existing = await _txn.Connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                FindEntityByIdentitySql,
                new { EntityTypeId = entityTypeId, IdentityValue = identityValue },
                _txn.Transaction,
                cancellationToken: cancellationToken));
            if (existing is { } id)
            {
                return id;
            }
        }

        var created = await _txn.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertEntitySql,
            new { EntityTypeId = entityTypeId, DisplayName = displayName, IdentityValue = identityValue },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        var afterValue = JsonSerializer.Serialize(new { display_name = displayName, identity_value = identityValue, source });
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertEntityCreatedLogSql,
            new { EntityInstanceId = created, AfterValue = afterValue },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        return created;
    }

    public async Task<Guid> AppendDocumentEntityLinkAsync(DocumentEntityLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        return await _txn.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertDocumentEntityLinkSql,
            new
            {
                link.ManagedDocumentId,
                link.EntityId,
                link.Role,
                link.RelationType,
                link.ConfidenceScore,
                link.Source,
                link.OperatorIdentity,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await _txn.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _txn.DisposeAsync();

    private async Task EnsureReadCommittedPinnedAsync(CancellationToken cancellationToken)
    {
        if (_isolationPinned)
        {
            return;
        }

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            EnsureReadCommittedSql,
            transaction: _txn.Transaction,
            cancellationToken: cancellationToken));
        _isolationPinned = true;
    }
}
