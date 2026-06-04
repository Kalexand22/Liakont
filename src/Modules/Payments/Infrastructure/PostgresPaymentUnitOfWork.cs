namespace Liakont.Modules.Payments.Infrastructure;

using System;
using Dapper;
using Liakont.Modules.Payments.Application;
using Liakont.Modules.Payments.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module Payments (item TRK04), ouverte sur la base DU TENANT (la connexion = le
/// tenant — database-per-tenant, blueprint §7). L'agrégat et sa piste d'audit (append-only) sont écrits dans
/// la MÊME transaction. Le journal <c>payment_aggregate_events</c> est immuable : son immuabilité est
/// garantie par un trigger base (CLAUDE.md n°4), jamais par un chemin de code.
/// </summary>
internal sealed class PostgresPaymentUnitOfWork : IPaymentUnitOfWork
{
    private const string InsertPaymentIfAbsentSql = """
        INSERT INTO payments.payments
            (id, payment_date, amount, method, related_document_number, source_reference, received_utc)
        VALUES
            (@Id, @PaymentDate, @Amount, @Method, @RelatedDocumentNumber, @SourceReference, @ReceivedUtc)
        ON CONFLICT (id) DO NOTHING
        """;

    private const string InsertAggregateIfAbsentSql = """
        INSERT INTO payments.payment_aggregates
            (id, period, aggregate_date, vat_rate, taxable_base, vat_amount, state, created_utc, last_update_utc)
        VALUES
            (@Id, @Period, @AggregateDate, @VatRate, @TaxableBase, @VatAmount, @State, @CreatedUtc, @LastUpdateUtc)
        ON CONFLICT (id) DO NOTHING
        """;

    private const string UpsertAggregateSql = """
        INSERT INTO payments.payment_aggregates
            (id, period, aggregate_date, vat_rate, taxable_base, vat_amount, state, created_utc, last_update_utc)
        VALUES
            (@Id, @Period, @AggregateDate, @VatRate, @TaxableBase, @VatAmount, @State, @CreatedUtc, @LastUpdateUtc)
        ON CONFLICT (id) DO UPDATE SET
            period          = excluded.period,
            aggregate_date  = excluded.aggregate_date,
            vat_rate        = excluded.vat_rate,
            taxable_base    = excluded.taxable_base,
            vat_amount      = excluded.vat_amount,
            state           = excluded.state,
            last_update_utc = excluded.last_update_utc
        """;

    private const string SelectAggregateForUpdateSql = """
        SELECT id, period, aggregate_date, vat_rate, taxable_base, vat_amount, state, created_utc, last_update_utc
        FROM payments.payment_aggregates
        WHERE id = @Id
        FOR UPDATE
        """;

    private const string InsertAggregateEventSql = """
        INSERT INTO payments.payment_aggregate_events
            (id, aggregate_id, timestamp_utc, event_type, state, detail, payload_snapshot, pa_response_snapshot)
        VALUES
            (@Id, @AggregateId, @TimestampUtc, @EventType, @State, @Detail, @PayloadSnapshot::jsonb, @PaResponseSnapshot::jsonb)
        """;

    private readonly TransactionScope _txn;

    private PostgresPaymentUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresPaymentUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, cancellationToken);
        return new PostgresPaymentUnitOfWork(txn);
    }

    public async Task<bool> SavePaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var inserted = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertPaymentIfAbsentSql,
            new
            {
                payment.Id,
                payment.PaymentDate,
                payment.Amount,
                payment.Method,
                payment.RelatedDocumentNumber,
                payment.SourceReference,
                payment.ReceivedUtc,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        return inserted > 0;
    }

    public async Task<bool> CreateAggregateAsync(PaymentAggregate aggregate, PaymentAggregateEvent genesisEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(genesisEvent);

        var inserted = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertAggregateIfAbsentSql,
            ToAggregateParameters(aggregate),
            _txn.Transaction,
            cancellationToken: cancellationToken));

        // Idempotence sur l'identifiant : si l'agrégat existait déjà, on n'écrit NI l'agrégat NI l'événement
        // de genèse — l'état déjà avancé n'est pas écrasé, l'audit pas dupliqué.
        if (inserted == 0)
        {
            return false;
        }

        await AppendAggregateEventAsync(genesisEvent, cancellationToken);
        return true;
    }

    public async Task<PaymentAggregate?> GetAggregateForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _txn.Connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            SelectAggregateForUpdateSql,
            new { Id = id },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        return row is null ? null : MapAggregate(row);
    }

    public async Task UpsertAggregateAsync(PaymentAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            UpsertAggregateSql,
            ToAggregateParameters(aggregate),
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task AppendAggregateEventAsync(PaymentAggregateEvent aggregateEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateEvent);

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertAggregateEventSql,
            new
            {
                aggregateEvent.Id,
                aggregateEvent.AggregateId,
                aggregateEvent.TimestampUtc,
                EventType = aggregateEvent.EventType.ToString(),
                State = aggregateEvent.State.ToString(),
                aggregateEvent.Detail,
                aggregateEvent.PayloadSnapshot,
                aggregateEvent.PaResponseSnapshot,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _txn.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static PaymentAggregate MapAggregate(dynamic row)
    {
        // Reconstitution de l'agrégat pour un read-modify-write (transition de transmission) : la colonne
        // textuelle `state` est reparsée vers l'énumération. Un libellé inconnu (rétro-incompatibilité) lève —
        // on n'avance jamais un état non modélisé en silence (CLAUDE.md n°3).
        return PaymentAggregate.Reconstitute(
            (Guid)row.id,
            (string)row.period,
            PaymentRowReader.ToDateOnly((object)row.aggregate_date),
            (decimal)row.vat_rate,
            (decimal)row.taxable_base,
            (decimal)row.vat_amount,
            Enum.Parse<PaymentAggregateState>((string)row.state),
            PaymentRowReader.ToDateTimeOffset((object)row.created_utc),
            PaymentRowReader.ToDateTimeOffset((object)row.last_update_utc));
    }

    private static object ToAggregateParameters(PaymentAggregate aggregate)
    {
        return new
        {
            aggregate.Id,
            aggregate.Period,
            aggregate.AggregateDate,
            aggregate.VatRate,
            aggregate.TaxableBase,
            aggregate.VatAmount,
            State = aggregate.State.ToString(),
            aggregate.CreatedUtc,
            aggregate.LastUpdateUtc,
        };
    }
}
