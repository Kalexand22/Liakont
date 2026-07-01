namespace Liakont.Modules.Ged.Infrastructure;

using System;
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
/// concurrence <c>pg_advisory_xact_lock</c> AVANT de superséder la valeur courante (RL-02).
/// </summary>
internal sealed class PostgresGedIndexUnitOfWork : IGedIndexUnitOfWork
{
    // Verrou consultatif transactionnel sur la clé document+axe (hashée). Tenu jusqu'au commit (variante xact) :
    // l'écrivain concurrent attend la fin de CETTE transaction avant de lire à son tour la valeur courante.
    private const string AdvisoryLockSql = "SELECT pg_advisory_xact_lock(hashtext(@LockKey))";

    // RL-02 — la supersession suivante est un read-modify-write (verrou → lire la courante → superséder) qui n'est
    // correcte que sous READ COMMITTED ; épinglé explicitement, cf. commentaire dans AppendAxisLinkAsync.
    private const string EnsureReadCommittedSql = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";

    // Valeur courante d'un axe MONO pour ce document (au plus une ligne sous la garde) — cible du supersedes_id.
    private const string CurrentAxisValueSql = """
        SELECT id
        FROM ged_index.current_axis_links
        WHERE managed_document_id = @ManagedDocumentId AND axis_id = @AxisId
        """;

    private const string InsertSql = """
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

    // Append PUR d'une relation entité↔entité (is_retraction=false, supersedes_id=null : GED24 n'écrit que des
    // relations dérivées « de valeur normale », la dévalidation par chaînage relève d'items ultérieurs).
    private const string InsertRelationSql = """
        INSERT INTO ged_index.entity_relations
            (from_entity_id, to_entity_id, relation_kind, relation_type, confidence_score, source)
        VALUES
            (@FromEntityId, @ToEntityId, @RelationKind, @RelationType, @ConfidenceScore, @Source)
        RETURNING id
        """;

    private readonly TransactionScope _txn;

    private PostgresGedIndexUnitOfWork(TransactionScope txn) => _txn = txn;

    public static async Task<PostgresGedIndexUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, cancellationToken);
        return new PostgresGedIndexUnitOfWork(txn);
    }

    public async Task<Guid> AppendAxisLinkAsync(DocumentAxisLink link, bool isSingleValued, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        Guid? supersedesId = null;

        if (isSingleValued)
        {
            var lockKey = FormattableString.Invariant($"{link.ManagedDocumentId:D}:{link.AxisId:D}");

            // RL-02 — la supersession est un read-modify-write (verrou → lire la courante → superséder) : elle
            // n'est correcte que sous READ COMMITTED, où le SELECT qui suit le verrou prend un snapshot FRAIS voyant
            // la ligne validée par l'écrivain précédent. Sous REPEATABLE READ/SERIALIZABLE le snapshot serait figé au
            // début de la transaction → deux écrivains superséderaient la même courante = DOUBLE valeur courante
            // permanente. On épingle donc explicitement l'isolation en tout premier statement de la transaction,
            // indépendamment du default serveur/rôle (TransactionScope, socle vendored, ouvre sans niveau explicite).
            await _txn.Connection.ExecuteAsync(new CommandDefinition(
                EnsureReadCommittedSql,
                transaction: _txn.Transaction,
                cancellationToken: cancellationToken));

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
            InsertSql,
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

    public async Task<Guid> AppendRelationAsync(EntityRelation relation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation);

        return await _txn.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertRelationSql,
            new
            {
                relation.FromEntityId,
                relation.ToEntityId,
                relation.RelationKind,
                relation.RelationType,
                relation.ConfidenceScore,
                relation.Source,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await _txn.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _txn.DisposeAsync();
}
