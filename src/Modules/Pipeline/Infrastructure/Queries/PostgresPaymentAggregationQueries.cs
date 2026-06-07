namespace Liakont.Modules.Pipeline.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper de la projection des agrégats jour×taux de l'e-reporting de paiement (PIP03a,
/// <c>pipeline.payment_aggregations</c>) sur la base DU TENANT courant (<see cref="IConnectionFactory"/>
/// route vers le tenant résolu — database-per-tenant, blueprint §7). Aucune requête cross-tenant n'est
/// possible : la connexion EST la frontière de tenant (CLAUDE.md n°9/17). La qualification fiscale (statut)
/// est seulement RELUE, jamais redérivée (calculée par PIP03a — CLAUDE.md n°2).
/// </summary>
public sealed class PostgresPaymentAggregationQueries : IPaymentAggregationQueries
{
    // Borne de sécurité (parité avec PostgresPipelineRunQueries.MaxLimit) — la fenêtre par période reste le
    // mode nominal côté WEB06 ; cette borne protège contre un appel sans période ramenant tout l'historique.
    private const int MaxResults = 5000;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresPaymentAggregationQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PaymentDailyAggregateDto>> GetAggregationsAsync(string? period, CancellationToken cancellationToken = default)
    {
        // Filtre de DATE pur (année-mois sur le jour d'encaissement), jamais une règle fiscale. Une période
        // absente ou mal formée ne filtre pas (l'endpoint rejette en amont une période non vide mal formée).
        var filtered = MonthPeriod.TryParse(period, out var start, out var endExclusive);
        var where = filtered ? "WHERE aggregate_date >= @Start AND aggregate_date < @EndExclusive" : string.Empty;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, aggregate_date, vat_rate, taxable_base, vat_amount, status, reason, computed_utc
            FROM pipeline.payment_aggregations
            {where}
            ORDER BY aggregate_date, vat_rate
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { Start = start, EndExclusive = endExclusive, Limit = MaxResults }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    private static PaymentDailyAggregateDto Map(dynamic row)
    {
        return new PaymentDailyAggregateDto
        {
            Id = (Guid)row.id,
            AggregateDate = ToDateOnly((object)row.aggregate_date),
            VatRate = (decimal)row.vat_rate,
            TaxableBase = (decimal)row.taxable_base,
            VatAmount = (decimal)row.vat_amount,
            Status = (string)row.status,
            Reason = (string?)row.reason,
            ComputedUtc = PipelineRowReader.ToDateTimeOffset((object)row.computed_utc),
        };
    }

    private static DateOnly ToDateOnly(object value) => value switch
    {
        DateOnly date => date,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new InvalidCastException($"Type de date inattendu lu en base : {value.GetType().FullName}."),
    };
}
