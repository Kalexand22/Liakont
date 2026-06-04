namespace Liakont.Modules.Payments.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Liakont.Modules.Payments.Domain.StateMachine;
using Xunit;

/// <summary>
/// Persistance des transitions de transmission d'un agrégat de paiement (TRK04) sur PostgreSQL réel : chaque
/// transmission journalise atomiquement l'état atteint ET ses snapshots de preuve (payload envoyé, réponse
/// PA) — même discipline que la piste d'audit des documents (F06 §3). Une transition non validée ne laisse
/// aucune trace ; une transition illégale est refusée AVANT toute écriture.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PaymentAggregateTransmissionIntegrationTests
{
    private static readonly AggregateTransmissionSnapshots Snapshots = new(
        payloadSnapshot: "{\"period\":\"2026-D2\",\"vatAmount\":33.00}",
        paResponseSnapshot: "{\"ledgerId\":\"L-42\",\"status\":\"accepted\"}");

    private readonly Fixtures.PaymentsDatabaseFixture _fixture;

    public PaymentAggregateTransmissionIntegrationTests(Fixtures.PaymentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transmission_Persists_State_And_Proof_Snapshots_As_Jsonb()
    {
        var harness = new PaymentsHarness(_fixture);
        var id = await SeedCalculatedAsync(harness);

        await TransitionAsync(harness, id, (agg, at) => agg.BeginSending(at), PaymentTestData.ReceivedAt.AddMinutes(1));
        await TransitionAsync(harness, id, (agg, at) => agg.MarkTransmitted(Snapshots, at), PaymentTestData.ReceivedAt.AddMinutes(2));

        var dto = await harness.Queries.GetAggregateByIdAsync(id);
        dto!.State.Should().Be(nameof(PaymentAggregateState.Transmitted));

        var events = await harness.Queries.GetAggregateEventsAsync(id);
        events.Should().HaveCount(3, "genèse + Sending + Transmitted.");
        events[0].EventType.Should().Be(nameof(PaymentAggregateEventType.AggregateCalculated));
        events[1].EventType.Should().Be(nameof(PaymentAggregateEventType.AggregateSending));
        events[2].EventType.Should().Be(nameof(PaymentAggregateEventType.AggregateTransmitted));

        // Les snapshots de la transmission sont relus sans perte depuis les colonnes jsonb.
        var (payload, paResponse) = await ReadSnapshotsAsync(harness, id, nameof(PaymentAggregateEventType.AggregateTransmitted));
        payload.Should().NotBeNull().And.Contain("2026-D2");
        paResponse.Should().NotBeNull().And.Contain("L-42");

        // Le simple passage en Sending n'archive aucun snapshot.
        var (sendingPayload, _) = await ReadSnapshotsAsync(harness, id, nameof(PaymentAggregateEventType.AggregateSending));
        sendingPayload.Should().BeNull();
    }

    [Fact]
    public async Task Transition_Is_Not_Visible_Until_Commit()
    {
        var harness = new PaymentsHarness(_fixture);
        var id = await SeedCalculatedAsync(harness);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var agg = await uow.GetAggregateForUpdateAsync(id);
            var evt = agg!.BeginSending(PaymentTestData.ReceivedAt.AddMinutes(1));
            await uow.UpsertAggregateAsync(agg);
            await uow.AppendAggregateEventAsync(evt);

            // PAS de CommitAsync : incident simulé avant validation.
        }

        var dto = await harness.Queries.GetAggregateByIdAsync(id);
        dto!.State.Should().Be(nameof(PaymentAggregateState.Calculated), "sans commit, ni l'état ni l'événement ne sont visibles.");
        (await harness.Queries.GetAggregateEventsAsync(id)).Should().ContainSingle("seule la genèse subsiste (atomicité).");
    }

    [Fact]
    public async Task Illegal_Transition_Leaves_Aggregate_And_Audit_Untouched()
    {
        var harness = new PaymentsHarness(_fixture);
        var id = await SeedCalculatedAsync(harness);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var agg = await uow.GetAggregateForUpdateAsync(id);

            // Calculated → Transmitted est interdit : l'exception survient AVANT toute écriture.
            var act = () => agg!.MarkTransmitted(Snapshots, PaymentTestData.ReceivedAt.AddMinutes(1));
            act.Should().Throw<InvalidPaymentAggregateTransitionException>();

            await uow.CommitAsync();
        }

        (await harness.Queries.GetAggregateByIdAsync(id))!.State.Should().Be(nameof(PaymentAggregateState.Calculated));
        (await harness.Queries.GetAggregateEventsAsync(id)).Should().ContainSingle("une transition refusée n'écrit aucun événement.");
    }

    private static async Task<Guid> SeedCalculatedAsync(PaymentsHarness harness)
    {
        var aggregate = PaymentTestData.NewAggregate();
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateAggregateAsync(aggregate, PaymentAggregateEvent.Genesis(aggregate.Id, PaymentTestData.ReceivedAt));
        await uow.CommitAsync();
        return aggregate.Id;
    }

    private static async Task TransitionAsync(
        PaymentsHarness harness,
        Guid id,
        Func<PaymentAggregate, DateTimeOffset, PaymentAggregateEvent> transition,
        DateTimeOffset occurredAtUtc)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var agg = await uow.GetAggregateForUpdateAsync(id);
        agg.Should().NotBeNull();

        var evt = transition(agg!, occurredAtUtc);
        await uow.UpsertAggregateAsync(agg!);
        await uow.AppendAggregateEventAsync(evt);
        await uow.CommitAsync();
    }

    private static async Task<(string? Payload, string? PaResponse)> ReadSnapshotsAsync(
        PaymentsHarness harness,
        Guid aggregateId,
        string eventType)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var row = await conn.QuerySingleAsync(
            """
            SELECT payload_snapshot::text     AS payload,
                   pa_response_snapshot::text AS paresponse
            FROM payments.payment_aggregate_events
            WHERE aggregate_id = @a AND event_type = @t
            """,
            new { a = aggregateId, t = eventType });

        return ((string?)row.payload, (string?)row.paresponse);
    }
}
