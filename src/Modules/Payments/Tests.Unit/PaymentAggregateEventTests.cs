namespace Liakont.Modules.Payments.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Xunit;

/// <summary>
/// Entrée d'audit <see cref="PaymentAggregateEvent"/> (F06 §3 / F09, TRK04) : la genèse porte l'état
/// <c>Calculated</c> sans snapshot ; une transition porte l'état atteint et, pour une transmission, ses
/// snapshots de preuve.
/// </summary>
public sealed class PaymentAggregateEventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Genesis_Is_Calculated_Without_Snapshots()
    {
        var aggregateId = Guid.NewGuid();

        var genesis = PaymentAggregateEvent.Genesis(aggregateId, T0);

        genesis.AggregateId.Should().Be(aggregateId);
        genesis.EventType.Should().Be(PaymentAggregateEventType.AggregateCalculated);
        genesis.State.Should().Be(PaymentAggregateState.Calculated);
        genesis.PayloadSnapshot.Should().BeNull();
        genesis.PaResponseSnapshot.Should().BeNull();
        genesis.TimestampUtc.Should().Be(T0);
    }

    [Fact]
    public void Transition_Carries_State_And_Optional_Snapshots()
    {
        var aggregateId = Guid.NewGuid();

        var evt = PaymentAggregateEvent.Transition(
            aggregateId,
            PaymentAggregateEventType.AggregateTransmitted,
            PaymentAggregateState.Transmitted,
            T0,
            "Transmis.",
            payloadSnapshot: "{\"p\":1}",
            paResponseSnapshot: "{\"r\":1}");

        evt.EventType.Should().Be(PaymentAggregateEventType.AggregateTransmitted);
        evt.State.Should().Be(PaymentAggregateState.Transmitted);
        evt.PayloadSnapshot.Should().Be("{\"p\":1}");
        evt.PaResponseSnapshot.Should().Be("{\"r\":1}");
    }
}
