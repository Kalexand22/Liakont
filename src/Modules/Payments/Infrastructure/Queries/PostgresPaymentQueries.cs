namespace Liakont.Modules.Payments.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Liakont.Modules.Payments.Contracts.DTOs;
using Liakont.Modules.Payments.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du module Payments (item TRK04) sur la base DU TENANT courant
/// (<see cref="IConnectionFactory"/> route vers le tenant résolu — database-per-tenant, blueprint §7).
/// Aucune requête cross-tenant n'est possible : la connexion EST la frontière de tenant (CLAUDE.md n°9/17).
/// </summary>
public sealed class PostgresPaymentQueries : IPaymentQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresPaymentQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PaymentDto?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, payment_date, amount, method, related_document_number, source_reference, received_utc
            FROM payments.payments
            WHERE id = @Id
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }

    public async Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, payment_date, amount, method, related_document_number, source_reference, received_utc
            FROM payments.payments
            ORDER BY payment_date, received_utc, id
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));

        return rows.Select(MapPayment).ToList();
    }

    public async Task<PaymentAggregateDto?> GetAggregateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, period, aggregate_date, vat_rate, taxable_base, vat_amount, state, created_utc, last_update_utc
            FROM payments.payment_aggregates
            WHERE id = @Id
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapAggregate(row);
    }

    public async Task<IReadOnlyList<PaymentAggregateEventDto>> GetAggregateEventsAsync(Guid aggregateId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, aggregate_id, timestamp_utc, event_type, state, detail,
                   payload_snapshot::text AS payload_snapshot,
                   pa_response_snapshot::text AS pa_response_snapshot
            FROM payments.payment_aggregate_events
            WHERE aggregate_id = @AggregateId
            ORDER BY timestamp_utc
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { AggregateId = aggregateId }, cancellationToken: cancellationToken));

        return rows.Select(MapEvent).ToList();
    }

    private static PaymentDto MapPayment(dynamic row)
    {
        return new PaymentDto
        {
            Id = (Guid)row.id,
            PaymentDate = PaymentRowReader.ToDateOnly((object)row.payment_date),
            Amount = (decimal)row.amount,
            Method = (string?)row.method,
            RelatedDocumentNumber = (string?)row.related_document_number,
            SourceReference = (string?)row.source_reference,
            ReceivedUtc = PaymentRowReader.ToDateTimeOffset((object)row.received_utc),
        };
    }

    private static PaymentAggregateDto MapAggregate(dynamic row)
    {
        return new PaymentAggregateDto
        {
            Id = (Guid)row.id,
            Period = (string)row.period,
            AggregateDate = PaymentRowReader.ToDateOnly((object)row.aggregate_date),
            VatRate = (decimal)row.vat_rate,
            TaxableBase = (decimal)row.taxable_base,
            VatAmount = (decimal)row.vat_amount,
            State = (string)row.state,
            CreatedUtc = PaymentRowReader.ToDateTimeOffset((object)row.created_utc),
            LastUpdateUtc = PaymentRowReader.ToDateTimeOffset((object)row.last_update_utc),
        };
    }

    private static PaymentAggregateEventDto MapEvent(dynamic row)
    {
        return new PaymentAggregateEventDto
        {
            Id = (Guid)row.id,
            AggregateId = (Guid)row.aggregate_id,
            TimestampUtc = PaymentRowReader.ToDateTimeOffset((object)row.timestamp_utc),
            EventType = (string)row.event_type,
            State = (string)row.state,
            Detail = (string?)row.detail,
            PayloadSnapshot = (string?)row.payload_snapshot,
            PaResponseSnapshot = (string?)row.pa_response_snapshot,
        };
    }
}
