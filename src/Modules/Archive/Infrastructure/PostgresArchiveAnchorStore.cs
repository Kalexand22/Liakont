namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance des ancrages temporels dans <c>documents.archive_anchors</c> (table de la migration V006,
/// TRK06), tenant-scopée par la connexion (<see cref="IConnectionFactory"/> → base du tenant courant).
/// Table APPEND-ONLY / WORM côté base (triggers anti UPDATE/DELETE/TRUNCATE de V006) — ce store n'expose
/// QUE des insertions et des lectures. Elle indexe les preuves stockées dans le coffre.
/// </summary>
internal sealed class PostgresArchiveAnchorStore : IArchiveAnchorStore
{
    private const string InsertSql = """
        INSERT INTO documents.archive_anchors
            (id, chain_head_entry_id, chain_head_hash, method, status, proof_path, anchored_utc, requested_utc)
        VALUES
            (@Id, @ChainHeadEntryId, @ChainHeadHash, @Method, @Status, @ProofPath, @AnchoredUtc, @RequestedUtc)
        """;

    private const string SelectAllSql = """
        SELECT id, chain_head_entry_id, chain_head_hash, method, status, proof_path, anchored_utc, requested_utc
        FROM documents.archive_anchors
        ORDER BY requested_utc ASC, id ASC
        """;

    private const string LatestForHeadSql = """
        SELECT id, chain_head_entry_id, chain_head_hash, method, status, proof_path, anchored_utc, requested_utc
        FROM documents.archive_anchors
        WHERE chain_head_hash = @ChainHeadHash AND method = @Method
        ORDER BY requested_utc DESC, id DESC
        LIMIT 1
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresArchiveAnchorStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ArchiveAnchorRecord> AppendAsync(
        Guid chainHeadEntryId,
        string chainHeadHash,
        TimestampAnchorMethod method,
        string status,
        string? proofPath,
        DateTimeOffset? anchoredUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(chainHeadHash);
        ArgumentException.ThrowIfNullOrEmpty(status);

        var anchorId = Guid.NewGuid();
        DateTimeOffset requestedUtc = Truncate(DateTimeOffset.UtcNow);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                Id = anchorId,
                ChainHeadEntryId = chainHeadEntryId,
                ChainHeadHash = chainHeadHash,
                Method = ToWire(method),
                Status = status,
                ProofPath = proofPath,
                AnchoredUtc = anchoredUtc?.UtcDateTime,
                RequestedUtc = requestedUtc.UtcDateTime,
            },
            cancellationToken: cancellationToken));

        return new ArchiveAnchorRecord(anchorId, chainHeadEntryId, chainHeadHash, method, status, proofPath, anchoredUtc, requestedUtc);
    }

    public async Task<IReadOnlyList<ArchiveAnchorRecord>> GetAnchorsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(new CommandDefinition(SelectAllSql, cancellationToken: cancellationToken));

        var records = new List<ArchiveAnchorRecord>();
        foreach (var row in rows)
        {
            records.Add(Map(row));
        }

        return records;
    }

    public async Task<ArchiveAnchorRecord?> GetLatestForHeadAsync(
        string chainHeadHash,
        TimestampAnchorMethod method,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(chainHeadHash);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            LatestForHeadSql,
            new { ChainHeadHash = chainHeadHash, Method = ToWire(method) },
            cancellationToken: cancellationToken));

        return row is null ? null : Map(row);
    }

    private static ArchiveAnchorRecord Map(dynamic row) => new(
        (Guid)row.id,
        (Guid)row.chain_head_entry_id,
        (string)row.chain_head_hash,
        FromWire((string)row.method),
        (string)row.status,
        row.proof_path as string,
        ToNullableUtc(row.anchored_utc),
        ToUtcOffset((object)row.requested_utc));

    private static string ToWire(TimestampAnchorMethod method) => method switch
    {
        TimestampAnchorMethod.None => "none",
        TimestampAnchorMethod.Rfc3161 => "rfc3161",
        TimestampAnchorMethod.OpenTimestamps => "opentimestamps",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Méthode d'ancrage inconnue."),
    };

    private static TimestampAnchorMethod FromWire(string wire) => wire switch
    {
        "none" => TimestampAnchorMethod.None,
        "rfc3161" => TimestampAnchorMethod.Rfc3161,
        "opentimestamps" => TimestampAnchorMethod.OpenTimestamps,
        _ => throw new InvalidOperationException($"Méthode d'ancrage inconnue lue en base : « {wire} »."),
    };

    private static DateTimeOffset? ToNullableUtc(object? value) =>
        value is null ? null : ToUtcOffset(value);

    private static DateTimeOffset ToUtcOffset(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Type d'horodatage inattendu lu en base : {value.GetType().FullName}."),
    };

    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);
}
