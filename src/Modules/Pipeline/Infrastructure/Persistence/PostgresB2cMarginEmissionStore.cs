namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture/lecture Dapper du journal d'émission e-reporting B2C de la marge (<c>pipeline.b2c_margin_emissions</c>,
/// B4) sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant — database-per-tenant,
/// blueprint §7). APPEND-ONLY : uniquement INSERT (jamais UPDATE/DELETE — la garde est aussi en base, V006) ;
/// l'anti-doublon est porté au grain document (attempt-once, décision D3). Le statut est persisté par NOM
/// d'énumération (lisibilité d'audit).
/// </summary>
public sealed class PostgresB2cMarginEmissionStore : IB2cMarginEmissionStore
{
    private readonly IConnectionFactory _connectionFactory;

    /// <summary>Construit le store sur la fabrique de connexion tenant-scopée.</summary>
    /// <param name="connectionFactory">Fabrique de connexion routée vers la base du tenant courant.</param>
    public PostgresB2cMarginEmissionStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> GetHandledDocumentIdsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = "SELECT DISTINCT document_id FROM pipeline.b2c_margin_emissions";

        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToHashSet();
    }

    /// <inheritdoc />
    public async Task AppendAsync(B2cMarginEmissionEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO pipeline.b2c_margin_emissions
                (id, document_id, source_reference, aggregate_date, currency, category, role,
                 content_hash, status, pa_emission_id, pa_response_snapshot, detail, created_utc)
            VALUES
                (@Id, @DocumentId, @SourceReference, @AggregateDate, @Currency, @Category, @Role,
                 @ContentHash, @Status, @PaEmissionId, @PaResponseSnapshot, @Detail, @CreatedUtc)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                entry.DocumentId,
                entry.SourceReference,
                entry.AggregateDate,
                Currency = entry.CurrencyCode,
                entry.Category,
                entry.Role,
                entry.ContentHash,
                Status = entry.Status.ToString(),
                entry.PaEmissionId,
                entry.PaResponseSnapshot,
                entry.Detail,
                CreatedUtc = DateTimeOffset.UtcNow,
            },
            cancellationToken: cancellationToken));
    }
}
