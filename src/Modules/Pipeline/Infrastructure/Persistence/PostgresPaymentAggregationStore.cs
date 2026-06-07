namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.Payments;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture/lecture Dapper de la projection des agrégats de paiement (<c>pipeline.payment_aggregations</c>,
/// PIP03a) sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant —
/// database-per-tenant, blueprint §7). PROJECTION RECALCULÉE : l'upsert remplace base/TVA/statut par
/// (jour, taux). Le statut est persisté par NOM d'énumération (lisibilité d'audit). Montants en numeric
/// (jamais float — CLAUDE.md n°1).
/// </summary>
public sealed class PostgresPaymentAggregationStore : IPaymentAggregationStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresPaymentAggregationStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ReplaceAllAsync(IReadOnlyList<PaymentDailyAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        using var transaction = conn.BeginTransaction();

        // PROJECTION recalculée INTÉGRALEMENT : on purge puis on ré-insère, atomiquement — aucune clé
        // (jour, taux) périmée ne survit (un encaissement re-daté/retiré côté source ne laisse pas d'agrégat
        // fantôme). C'est une projection, pas une table d'audit : la purge est légitime ici (CLAUDE.md n°4).
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pipeline.payment_aggregations",
            transaction: transaction,
            cancellationToken: cancellationToken));

        const string insertSql = """
            INSERT INTO pipeline.payment_aggregations
                (id, aggregate_date, vat_rate, taxable_base, vat_amount, status, reason, computed_utc)
            VALUES
                (@Id, @AggregateDate, @VatRate, @TaxableBase, @VatAmount, @Status, @Reason, @ComputedUtc)
            """;

        var computedUtc = DateTimeOffset.UtcNow;
        foreach (var aggregate in aggregates)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                insertSql,
                new
                {
                    Id = Guid.NewGuid(),
                    AggregateDate = aggregate.Date,
                    VatRate = aggregate.Rate,
                    aggregate.TaxableBase,
                    aggregate.VatAmount,
                    Status = aggregate.Status.ToString(),
                    aggregate.Reason,
                    ComputedUtc = computedUtc,
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<PaymentDailyAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT aggregate_date, vat_rate, taxable_base, vat_amount, status, reason
            FROM pipeline.payment_aggregations
            ORDER BY aggregate_date, vat_rate
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    private static PaymentDailyAggregate Map(dynamic row)
    {
        return new PaymentDailyAggregate
        {
            Date = ToDateOnly((object)row.aggregate_date),
            Rate = (decimal)row.vat_rate,
            TaxableBase = (decimal)row.taxable_base,
            VatAmount = (decimal)row.vat_amount,
            Status = Enum.Parse<PaymentAggregationStatus>((string)row.status),
            Reason = (string?)row.reason,
        };
    }

    private static DateOnly ToDateOnly(object value) => value switch
    {
        DateOnly date => date,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new InvalidOperationException($"Type de date inattendu : {value.GetType()}"),
    };
}
