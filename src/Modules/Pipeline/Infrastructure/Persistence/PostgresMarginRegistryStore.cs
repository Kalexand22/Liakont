namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture Dapper du registre de la marge à déclarer (<c>pipeline.margin_registry</c>, Livrable 2) sur la base
/// DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant — database-per-tenant, blueprint §7).
/// PROJECTION recalculable (≠ <see cref="PostgresB2cMarginEmissionStore"/>, WORM) : <see cref="UpsertAsync"/>
/// fait un <c>INSERT … ON CONFLICT (document_id) DO UPDATE</c> (un doc = un taux), <see cref="DeleteAsync"/>
/// retire l'entrée d'un document qui n'est plus au régime de la marge. Montants/taux en <see cref="decimal"/>
/// (numeric en base) — jamais float (CLAUDE.md n°1). <c>computed_utc</c> est posé/rafraîchi par la base (<c>now()</c>).
/// </summary>
public sealed class PostgresMarginRegistryStore : IMarginRegistryStore
{
    private readonly IConnectionFactory _connectionFactory;

    /// <summary>Construit le store sur la fabrique de connexion tenant-scopée.</summary>
    /// <param name="connectionFactory">Fabrique de connexion routée vers la base du tenant courant.</param>
    public PostgresMarginRegistryStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(MarginRegistryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO pipeline.margin_registry
                (document_id, issue_date, currency, vat_rate, margin_base_ht, margin_vat, computed_utc)
            VALUES
                (@DocumentId, @IssueDate, @Currency, @VatRate, @MarginBaseHt, @MarginVat, now())
            ON CONFLICT (document_id) DO UPDATE SET
                issue_date     = EXCLUDED.issue_date,
                currency       = EXCLUDED.currency,
                vat_rate       = EXCLUDED.vat_rate,
                margin_base_ht = EXCLUDED.margin_base_ht,
                margin_vat     = EXCLUDED.margin_vat,
                computed_utc   = now()
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entry.DocumentId,
                IssueDate = entry.IssueDate.ToDateTime(TimeOnly.MinValue),
                Currency = entry.CurrencyCode,
                entry.VatRate,
                entry.MarginBaseHt,
                entry.MarginVat,
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = "DELETE FROM pipeline.margin_registry WHERE document_id = @DocumentId";

        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
    }
}
