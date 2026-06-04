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
/// PAR TENANT, une seule clé suffit) qui SÉRIALISE les ajouts du tenant. Sous ce verrou, on lit d'abord
/// si une ligne existe déjà pour ce <c>package_path</c> (idempotence) ; sinon on lit la tête, on dérive le
/// <c>chain_hash</c> et un <c>archived_utc</c> STRICTEMENT croissant, puis on insère — sans aucune E/S
/// coffre dans la transaction (le manifest est écrit par le service, APRÈS commit, déterministe depuis
/// la ligne committée).
/// </summary>
internal sealed class PostgresArchiveEntryStore : IArchiveEntryStore
{
    // Clé de verrou consultatif de la chaîne d'archive (constante : 'ARCH'). La base étant par tenant,
    // une clé globale suffit à sérialiser tous les ajouts du tenant courant.
    private const long ArchiveChainLockKey = 0x41524348;

    private const string ExistingByPathSql = """
        SELECT id, document_id, package_path, package_hash, chain_hash, archived_utc
        FROM documents.archive_entries
        WHERE package_path = @PackagePath
        LIMIT 1
        """;

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

    public async Task<ArchiveEntryRecord> ReserveAsync(
        Guid documentId,
        string packagePath,
        string packageHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        ArgumentException.ThrowIfNullOrEmpty(packageHash);

        await using var scope = await TransactionScope.BeginAsync(_connectionFactory, cancellationToken);

        await scope.Connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@Key)",
            new { Key = ArchiveChainLockKey },
            scope.Transaction,
            cancellationToken: cancellationToken));

        // Idempotence : si une ligne existe déjà pour ce chemin, on la retourne sans ré-insérer.
        // (package_path n'a pas encore d'index sur documents.archive_entries — acceptable pour le
        // faible taux d'ajout par tenant en V1 ; un index peut être ajouté plus tard par ops.)
        var existing = await scope.Connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            ExistingByPathSql,
            new { PackagePath = packagePath },
            scope.Transaction,
            cancellationToken: cancellationToken));

        if (existing is not null)
        {
            await scope.CommitAsync(cancellationToken);
            return new ArchiveEntryRecord(
                (Guid)existing.id,
                (Guid)existing.document_id,
                (string)existing.package_path,
                (string)existing.package_hash,
                (string)existing.chain_hash,
                ToUtcOffset((object)existing.archived_utc));
        }

        var head = await scope.Connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            HeadSql, transaction: scope.Transaction, cancellationToken: cancellationToken));

        string? previousChainHash = head is null ? null : (string?)head.chain_hash;
        DateTimeOffset? previousArchivedUtc = head is null ? null : ToUtcOffset((object)head.archived_utc);

        string chainHash = HashChain.Next(previousChainHash, packageHash);
        DateTimeOffset archivedUtc = NextArchivedUtc(previousArchivedUtc);

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
            // Genèse : tronquer à la microseconde pour que la valeur soit bit-identique à ce que PostgreSQL
            // restockera et renverra (timestamptz à précision µs — 10 ticks = 1 µs).
            return Truncate(now);
        }

        // Strictement croissant : au moins 1 microseconde (précision PostgreSQL timestamptz) après la tête.
        // previous est déjà µs-aligné (relu depuis la base). On tronque now avant de comparer, afin que la
        // valeur écrite ET la valeur relue depuis la base soient bit-identiques (idempotence de rejeu).
        DateTimeOffset floor = previous.AddTicks(10);
        DateTimeOffset candidate = Truncate(now);
        return candidate > floor ? candidate : floor;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

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
