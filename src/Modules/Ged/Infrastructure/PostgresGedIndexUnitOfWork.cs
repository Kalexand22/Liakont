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

    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await _txn.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _txn.DisposeAsync();
}
