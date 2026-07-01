namespace Liakont.Modules.Pipeline.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du journal d'émission e-reporting B2C de la marge (B4, <c>pipeline.b2c_margin_emissions</c>)
/// sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant résolu —
/// database-per-tenant ; la connexion EST la frontière de tenant, CLAUDE.md n°9/17). Aucune lecture
/// cross-tenant n'est possible. Lecture SEULE : la classification (statut) est seulement relue, jamais
/// redérivée (CLAUDE.md n°2).
/// <para>
/// Le journal est append-only AU GRAIN DOCUMENT (une entrée <c>Pending</c> avant le POST puis l'issue, par
/// document). La vue REGROUPE par <c>emission_batch_id</c> (l'identité d'une TRANSMISSION réelle — un par POST)
/// et ne garde que la DERNIÈRE entrée de chaque lot (état courant : <c>ROW_NUMBER</c> sur <c>created_utc</c>,
/// <c>seq</c>) ; le nombre de pièces est le <c>COUNT(DISTINCT document_id)</c> du lot (CTE séparée — PostgreSQL
/// n'admet pas <c>COUNT(DISTINCT)</c> en fonction de fenêtre). On NE regroupe PAS par <c>content_hash</c> : il
/// n'est pas unique par transmission (deux POST distincts d'un même contenu — document tardif → nouvel agrégat —
/// le partagent), les fusionner masquerait une transmission réelle sur cette surface d'audit.
/// </para>
/// </summary>
public sealed class PostgresB2cMarginEmissionQueries : IB2cMarginEmissionQueries
{
    // Borne de sécurité (parité PostgresPaymentAggregationQueries) : protège un appel sans période. L'ORDER BY
    // aggregate_date DESC garantit que la troncature ne supprime que les agrégats les plus anciens.
    private const int MaxResults = 5000;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresB2cMarginEmissionQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<B2cMarginEmissionAggregateDto>> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
    {
        // Filtre de DATE pur (année-mois sur le jour de l'agrégat), jamais une règle fiscale. Une période
        // absente ou mal formée ne filtre pas. aggregate_date est constant au sein d'un lot d'émission : le filtre
        // garde ou écarte une transmission ENTIÈRE (cohérence du regroupement et du comptage des pièces).
        var filtered = MonthPeriod.TryParse(period, out var start, out var endExclusive);
        var where = filtered ? "WHERE aggregate_date >= @Start AND aggregate_date < @EndExclusive" : string.Empty;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            WITH filtered AS (
                SELECT emission_batch_id, content_hash, aggregate_date, currency, category, role, status,
                       pa_emission_id, detail, created_utc, seq, document_id
                FROM pipeline.b2c_margin_emissions
                {where}
            ),
            ranked AS (
                SELECT emission_batch_id, content_hash, aggregate_date, currency, category, role, status,
                       pa_emission_id, detail, created_utc,
                       ROW_NUMBER() OVER (PARTITION BY emission_batch_id ORDER BY created_utc DESC, seq DESC) AS rn
                FROM filtered
            ),
            counts AS (
                SELECT emission_batch_id, COUNT(DISTINCT document_id) AS document_count
                FROM filtered
                GROUP BY emission_batch_id
            )
            SELECT r.emission_batch_id, r.content_hash, r.aggregate_date, r.currency, r.category, r.role, r.status,
                   r.pa_emission_id, r.detail, r.created_utc, c.document_count
            FROM ranked r
            JOIN counts c ON c.emission_batch_id = r.emission_batch_id
            WHERE r.rn = 1
            ORDER BY r.aggregate_date DESC, r.currency, r.created_utc DESC, r.emission_batch_id
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { Start = start, EndExclusive = endExclusive, Limit = MaxResults }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    public async Task<B2cMarginEmissionDetailDto?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // État COURANT du lot : la DERNIÈRE entrée (ROW_NUMBER sur created_utc, seq), avec le snapshot brut de
        // réponse PA. Même logique de regroupement que GetEmissionsAsync, bornée à UN emission_batch_id.
        const string aggregateSql = """
            WITH ranked AS (
                SELECT emission_batch_id, content_hash, aggregate_date, currency, category, role, status,
                       pa_emission_id, pa_response_snapshot, detail, created_utc,
                       ROW_NUMBER() OVER (PARTITION BY emission_batch_id ORDER BY created_utc DESC, seq DESC) AS rn
                FROM pipeline.b2c_margin_emissions
                WHERE emission_batch_id = @EmissionBatchId
            )
            SELECT emission_batch_id, content_hash, aggregate_date, currency, category, role, status,
                   pa_emission_id, pa_response_snapshot, detail, created_utc
            FROM ranked
            WHERE rn = 1
            """;

        var aggregate = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            aggregateSql, new { EmissionBatchId = emissionBatchId }, cancellationToken: cancellationToken));

        if (aggregate is null)
        {
            return null;
        }

        // Pièces du lot : documents DISTINCTS (un document écrit plusieurs entrées — Pending puis issue), triés
        // par référence source pour un affichage déterministe.
        const string documentsSql = """
            SELECT DISTINCT document_id, source_reference
            FROM pipeline.b2c_margin_emissions
            WHERE emission_batch_id = @EmissionBatchId
            ORDER BY source_reference
            """;

        var documentRows = await conn.QueryAsync(new CommandDefinition(
            documentsSql, new { EmissionBatchId = emissionBatchId }, cancellationToken: cancellationToken));

        var documents = documentRows
            .Select(d => new B2cMarginEmissionDocumentDto
            {
                DocumentId = (Guid)d.document_id,
                SourceReference = (string)d.source_reference,
            })
            .ToList();

        return new B2cMarginEmissionDetailDto
        {
            EmissionBatchId = (Guid)aggregate.emission_batch_id,
            ContentHash = (string)aggregate.content_hash,
            AggregateDate = ToDateOnly((object)aggregate.aggregate_date),
            CurrencyCode = (string)aggregate.currency,
            Category = (string)aggregate.category,
            Role = (string)aggregate.role,
            Status = (string)aggregate.status,
            PaEmissionId = (string?)aggregate.pa_emission_id,
            Detail = (string?)aggregate.detail,
            PaResponseSnapshot = (string?)aggregate.pa_response_snapshot,
            LastActivityUtc = PipelineRowReader.ToDateTimeOffset((object)aggregate.created_utc),
            Documents = documents,
        };
    }

    public async Task<Guid?> GetEmissionBatchIdForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Lot de la DERNIÈRE transmission ISSUED qui a inclus ce document (created_utc, seq décroissants). Le journal
        // porte une entrée par (document × POST) ; on ne retient que les issues confirmées. Un document tardif
        // ré-agrégé (D3) appartient à son POST le plus récent. Aucun événement d'audit n'est requis : la liaison
        // vit dans le journal d'émission — donc le lot se résout AUSSI pour un document rétro-corrigé par V012
        // (BUG-24, ADR-0037 §4). Lecture seule, tenant-scopée (la connexion EST le tenant).
        const string sql = """
            SELECT emission_batch_id
            FROM pipeline.b2c_margin_emissions
            WHERE document_id = @DocumentId
              AND status = 'Issued'
            ORDER BY created_utc DESC, seq DESC
            LIMIT 1
            """;

        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
    }

    private static B2cMarginEmissionAggregateDto Map(dynamic row)
    {
        return new B2cMarginEmissionAggregateDto
        {
            EmissionBatchId = (Guid)row.emission_batch_id,
            ContentHash = (string)row.content_hash,
            AggregateDate = ToDateOnly((object)row.aggregate_date),
            CurrencyCode = (string)row.currency,
            Category = (string)row.category,
            Role = (string)row.role,
            Status = (string)row.status,
            PaEmissionId = (string?)row.pa_emission_id,
            Detail = (string?)row.detail,
            DocumentCount = (int)(long)row.document_count,
            LastActivityUtc = PipelineRowReader.ToDateTimeOffset((object)row.created_utc),
        };
    }

    private static DateOnly ToDateOnly(object value) => value switch
    {
        DateOnly date => date,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new InvalidCastException($"Type de date inattendu lu en base : {value.GetType().FullName}."),
    };
}
