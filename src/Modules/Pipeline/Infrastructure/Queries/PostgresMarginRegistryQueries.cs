namespace Liakont.Modules.Pipeline.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dapper;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du registre de la marge à déclarer (<c>pipeline.margin_registry</c>, Livrable 2) sur la base
/// DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant résolu — database-per-tenant ; la
/// connexion EST la frontière de tenant, CLAUDE.md n°9/17). Aucune lecture cross-tenant n'est possible. Lecture
/// SEULE : la marge est seulement RELUE/SOMMÉE par mois × devise × taux, jamais redérivée (CLAUDE.md n°2).
/// </summary>
public sealed class PostgresMarginRegistryQueries : IMarginRegistryQueries
{
    // Borne de sécurité (parité PostgresB2cMarginEmissionQueries) : protège un appel sans période. L'ORDER BY
    // mois DESC garantit que la troncature ne supprime que les mois les plus anciens.
    private const int MaxResults = 5000;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresMarginRegistryQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MarginRegistryMonthlyDto>> GetMonthlyAsync(string? period, CancellationToken cancellationToken = default)
    {
        // Filtre de DATE pur (année-mois sur le jour d'émission), jamais une règle fiscale. Une période absente
        // ou mal formée ne filtre pas (toutes les périodes, bornées par MaxResults).
        var filtered = MonthPeriod.TryParse(period, out var start, out var endExclusive);
        var where = filtered ? "WHERE issue_date >= @Start AND issue_date < @EndExclusive" : string.Empty;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT to_char(issue_date, 'YYYY-MM') AS period,
                   currency,
                   vat_rate,
                   SUM(margin_base_ht) AS margin_base_ht,
                   SUM(margin_vat)     AS margin_vat,
                   COUNT(*)            AS document_count
            FROM pipeline.margin_registry
            {where}
            GROUP BY to_char(issue_date, 'YYYY-MM'), currency, vat_rate
            ORDER BY period DESC, currency, vat_rate
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { Start = start, EndExclusive = endExclusive, Limit = MaxResults }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    private static MarginRegistryMonthlyDto Map(dynamic row)
    {
        return new MarginRegistryMonthlyDto
        {
            Period = (string)row.period,
            CurrencyCode = (string)row.currency,
            RatePercent = (decimal)row.vat_rate,
            MarginBaseHt = (decimal)row.margin_base_ht,
            MarginVat = (decimal)row.margin_vat,
            DocumentCount = (int)(long)row.document_count,
        };
    }
}
