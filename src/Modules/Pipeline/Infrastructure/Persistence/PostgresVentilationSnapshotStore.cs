namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture/lecture Dapper du snapshot de ventilation TVA (<c>pipeline.ventilation_snapshots</c>, ADR-0015)
/// sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant — database-per-tenant,
/// blueprint §7). APPEND-ONLY (triggers base, INV-VENTILATION-003) ; l'écriture est IDEMPOTENTE sur
/// (document_id, mapping_version) via <c>ON CONFLICT DO NOTHING</c>. Les lignes sont sérialisées en jsonb
/// avec montants/taux en CHAÎNES invariantes — JAMAIS de float (CLAUDE.md n°1) ; reparsées en decimal en lecture.
/// </summary>
public sealed class PostgresVentilationSnapshotStore : IVentilationSnapshotStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresVentilationSnapshotStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> SaveAsync(VentilationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO pipeline.ventilation_snapshots
                (id, document_id, document_number, source_reference, operation_category,
                 mapping_version, lines, created_utc)
            VALUES
                (@Id, @DocumentId, @DocumentNumber, @SourceReference, @OperationCategory,
                 @MappingVersion, @Lines::jsonb, @CreatedUtc)
            ON CONFLICT (document_id, mapping_version) DO NOTHING
            """;

        var rows = await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                snapshot.DocumentId,
                snapshot.DocumentNumber,
                snapshot.SourceReference,
                OperationCategory = (int)snapshot.OperationCategory,
                snapshot.MappingVersion,
                Lines = SerializeLines(snapshot.Lines),
                snapshot.CreatedUtc,
            },
            cancellationToken: cancellationToken));

        return rows == 1;
    }

    public async Task<VentilationSnapshot?> GetAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingVersion);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, document_id, document_number, source_reference, operation_category,
                   mapping_version, lines, created_utc
            FROM pipeline.ventilation_snapshots
            WHERE document_id = @DocumentId AND mapping_version = @MappingVersion
            """;

        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            sql,
            new { DocumentId = documentId, MappingVersion = mappingVersion },
            cancellationToken: cancellationToken));

        return row is null ? null : Map(row);
    }

    private static VentilationSnapshot Map(dynamic row)
    {
        return new VentilationSnapshot
        {
            DocumentId = (Guid)row.document_id,
            DocumentNumber = (string)row.document_number,
            SourceReference = (string)row.source_reference,
            OperationCategory = (OperationCategory)(int)row.operation_category,
            MappingVersion = (string)row.mapping_version,
            Lines = DeserializeLines((string)row.lines),
            CreatedUtc = ToDateTimeOffset((object)row.created_utc),
        };
    }

    private static string SerializeLines(IReadOnlyList<VentilationLine> lines)
    {
        var payload = new List<LineJson>(lines.Count);
        foreach (var line in lines)
        {
            payload.Add(new LineJson
            {
                Rate = line.Rate?.ToString(CultureInfo.InvariantCulture),
                Base = line.TaxableBase.ToString(CultureInfo.InvariantCulture),
                Vat = line.VatAmount.ToString(CultureInfo.InvariantCulture),
                Category = line.Category,
            });
        }

        return JsonSerializer.Serialize(payload);
    }

    private static List<VentilationLine> DeserializeLines(string json)
    {
        var payload = JsonSerializer.Deserialize<List<LineJson>>(json) ?? new List<LineJson>();
        var lines = new List<VentilationLine>(payload.Count);
        foreach (var item in payload)
        {
            decimal? rate = item.Rate is null ? null : decimal.Parse(item.Rate, CultureInfo.InvariantCulture);
            var taxableBase = decimal.Parse(item.Base ?? "0", CultureInfo.InvariantCulture);
            var vatAmount = decimal.Parse(item.Vat ?? "0", CultureInfo.InvariantCulture);
            lines.Add(VentilationLine.Create(rate, taxableBase, vatAmount, item.Category));
        }

        return lines;
    }

    private static DateTimeOffset ToDateTimeOffset(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
        _ => throw new InvalidOperationException($"Type d'horodatage inattendu : {value.GetType()}"),
    };

    private sealed class LineJson
    {
        public string? Rate { get; set; }

        public string? Base { get; set; }

        public string? Vat { get; set; }

        public string? Category { get; set; }
    }
}
