namespace Liakont.Modules.Archive.Infrastructure;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance des entrées du coffre dans <c>documents.archive_entries</c> (table TRK01 « alimentée par
/// TRK05 », voir le commentaire de sa migration V005). Tenant-scopée par la connexion
/// (<see cref="IConnectionFactory"/> → base du tenant courant ; la table n'a donc pas de colonne tenant).
/// La table est WORM côté base (triggers anti UPDATE/DELETE/TRUNCATE de V005) — ce store n'expose QUE des
/// insertions et des lectures.
///
/// Ordonnancement strict de la chaîne : chaque ajout prend un <c>pg_advisory_xact_lock</c> (la base étant
/// PAR TENANT, une seule clé suffit) qui SÉRIALISE les ajouts du tenant. Sous ce verrou, on lit la tête,
/// on dérive le <c>chain_hash</c> et un <c>archived_utc</c> STRICTEMENT croissant, on fait écrire les
/// artefacts du coffre, puis on insère — le tout dans une transaction (atomique).
/// </summary>
internal sealed class PostgresArchiveEntryStore : IArchiveEntryStore
{
    // Clé de verrou consultatif de la chaîne d'archive (constante : 'ARCH'). La base étant par tenant,
    // une clé globale suffit à sérialiser tous les ajouts du tenant courant.
    private const long ArchiveChainLockKey = 0x41524348;

    private const string HeadSql = """
        SELECT chain_hash, archived_utc
        FROM documents.archive_entries
        ORDER BY archived_utc DESC, id DESC
        LIMIT 1
        """;

    private const string InsertSql = """
        INSERT INTO documents.archive_entries
            (id, document_id, package_path, package_hash, chain_hash, archived_utc)
        VALUES
            (@Id, @DocumentId, @PackagePath, @PackageHash, @ChainHash, @ArchivedUtc)
        """;

    private const string ChainSql = """
        SELECT id, document_id, package_path, package_hash, chain_hash, archived_utc
        FROM documents.archive_entries
        ORDER BY archived_utc ASC, id ASC
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresArchiveEntryStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ArchiveEntryRecord> AppendAsync(
        Guid documentId,
        string packageHash,
        Func<ArchiveSealContext, CancellationToken, Task<string>> writeArtifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageHash);
        ArgumentNullException.ThrowIfNull(writeArtifacts);

        await using var scope = await TransactionScope.BeginAsync(_connectionFactory, cancellationToken);

        await scope.Connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@Key)",
            new { Key = ArchiveChainLockKey },
            scope.Transaction,
            cancellationToken: cancellationToken));

        var head = await scope.Connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            HeadSql, transaction: scope.Transaction, cancellationToken: cancellationToken));

        string? previousChainHash = head is null ? null : (string?)head.chain_hash;
        DateTimeOffset? previousArchivedUtc = head is null ? null : ToUtcOffset((object)head.archived_utc);

        string chainHash = HashChain.Next(previousChainHash, packageHash);
        DateTimeOffset archivedUtc = NextArchivedUtc(previousArchivedUtc);

        string packagePath = await writeArtifacts(new ArchiveSealContext(chainHash, archivedUtc), cancellationToken);

        var entryId = Guid.NewGuid();
        await scope.Connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                Id = entryId,
                DocumentId = documentId,
                PackagePath = packagePath,
                PackageHash = packageHash,
                ChainHash = chainHash,
                ArchivedUtc = archivedUtc.UtcDateTime,
            },
            scope.Transaction,
            cancellationToken: cancellationToken));

        await scope.CommitAsync(cancellationToken);
        return new ArchiveEntryRecord(entryId, documentId, packagePath, packageHash, chainHash, archivedUtc);
    }

    public async Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(new CommandDefinition(ChainSql, cancellationToken: cancellationToken));

        var records = new List<ArchiveEntryRecord>();
        foreach (var row in rows)
        {
            records.Add(new ArchiveEntryRecord(
                (Guid)row.id,
                (Guid)row.document_id,
                (string)row.package_path,
                (string)row.package_hash,
                (string)row.chain_hash,
                ToUtcOffset((object)row.archived_utc)));
        }

        return records;
    }

    private static DateTimeOffset NextArchivedUtc(DateTimeOffset? previousArchivedUtc)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (previousArchivedUtc is not { } previous)
        {
            return now;
        }

        // Strictement croissant : au moins 1 microseconde (précision PostgreSQL timestamptz) après la tête,
        // pour que l'ordre archived_utc reflète exactement l'ordre d'ajout (déterminisme de la chaîne).
        DateTimeOffset floor = previous.AddTicks(10);
        return now > floor ? now : floor;
    }

    private static DateTimeOffset ToUtcOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Type d'horodatage inattendu lu en base : {value.GetType().FullName}."),
        };
    }
}
