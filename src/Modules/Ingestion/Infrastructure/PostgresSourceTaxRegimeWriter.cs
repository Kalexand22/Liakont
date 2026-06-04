namespace Liakont.Modules.Ingestion.Infrastructure;

using System.Collections.Generic;
using Dapper;
using Liakont.Modules.Ingestion.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Upsert des régimes de TVA source observés (métadonnée de push, PIV04), base SYSTÈME (schéma
/// <c>ingestion</c>), scopé tenant. Idempotent : un même code cumule ses occurrences et rafraîchit son
/// libellé/horodatage. Le code source est conservé BRUT (jamais interprété, CLAUDE.md n°2).
/// </summary>
internal sealed class PostgresSourceTaxRegimeWriter : ISourceTaxRegimeWriter
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresSourceTaxRegimeWriter(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task UpsertAsync(string tenantId, IReadOnlyList<SourceTaxRegimeObservation> regimes, CancellationToken cancellationToken = default)
    {
        if (regimes.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO ingestion.source_tax_regimes (tenant_id, code, label, occurrences, last_seen_at)
            VALUES (@TenantId, @Code, @Label, @Occurrences, @LastSeenAt)
            ON CONFLICT (tenant_id, code) DO UPDATE
            SET occurrences  = ingestion.source_tax_regimes.occurrences + EXCLUDED.occurrences,
                label        = COALESCE(EXCLUDED.label, ingestion.source_tax_regimes.label),
                last_seen_at = EXCLUDED.last_seen_at
            """;

        var now = DateTimeOffset.UtcNow;

        await using var txn = await TransactionScope.BeginAsync(
            new SystemConnectionFactoryAdapter(_systemConnectionFactory),
            cancellationToken);

        foreach (var regime in regimes)
        {
            if (string.IsNullOrWhiteSpace(regime.Code))
            {
                continue; // le code source brut est obligatoire (clé du régime) ; on ignore l'entrée vide.
            }

            await txn.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    Code = regime.Code.Trim(),
                    regime.Label,
                    Occurrences = (long)regime.Occurrences,
                    LastSeenAt = now,
                },
                txn.Transaction,
                cancellationToken: cancellationToken));
        }

        await txn.CommitAsync(cancellationToken);
    }
}
