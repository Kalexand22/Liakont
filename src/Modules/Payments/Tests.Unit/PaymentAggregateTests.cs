namespace Liakont.Modules.Payments.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Liakont.Modules.Payments.Domain.StateMachine;
using Xunit;

/// <summary>
/// Agrégat jour × taux <see cref="PaymentAggregate"/> (F09 §5.3 / TRK04) : création en état
/// <c>Calculated</c>, garde-fous d'intégrité de stockage (montants 2 décimales, taux 4 décimales), montants
/// négatifs autorisés (rectificatif — F09 §5.4), et machine à états de transmission EXPLICITE (transitions
/// illégales refusées AVANT mutation ; états terminaux <c>Transmitted</c>/<c>RejectedByPa</c>).
/// </summary>
public sealed class PaymentAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly D0 = new(2026, 5, 14);

    private static readonly AggregateTransmissionSnapshots Snapshots = new(
        payloadSnapshot: "{\"period\":\"2026-D2\",\"vatAmount\":33.00}",
        paResponseSnapshot: "{\"ledgerId\":\"L-42\"}");

    [Fact]
    public void Create_Starts_In_Calculated_With_Source_Values()
    {
        var agg = PaymentAggregate.Create(Guid.NewGuid(), "2026-D2", D0, 0.2000m, 165.00m, 33.00m, T0);

        agg.State.Should().Be(PaymentAggregateState.Calculated);
        agg.Period.Should().Be("2026-D2");
        agg.AggregateDate.Should().Be(D0);
        agg.VatRate.Should().Be(0.2000m);
        agg.TaxableBase.Should().Be(165.00m);
        agg.VatAmount.Should().Be(33.00m);
        agg.CreatedUtc.Should().Be(T0);
        agg.LastUpdateUtc.Should().Be(T0);
    }

    [Fact]
    public void Create_Allows_Negative_Amounts_For_Rectification()
    {
        var agg = PaymentAggregate.Create(Guid.NewGuid(), "2026-D2", D0, 0.2000m, -100.00m, -20.00m, T0);

        agg.TaxableBase.Should().Be(-100.00m);
        agg.VatAmount.Should().Be(-20.00m, "un rectificatif (trop-perçu) produit un agrégat négatif (F09 §5.4).");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Requires_A_Period(string blank)
    {
        var act = () => PaymentAggregate.Create(Guid.NewGuid(), blank, D0, 0.2000m, 165.00m, 33.00m, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("period");
    }

    [Fact]
    public void Create_Rejects_Taxable_Base_With_More_Than_2_Decimals()
    {
        var act = () => PaymentAggregate.Create(Guid.NewGuid(), "2026-D2", D0, 0.2000m, 165.005m, 33.00m, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("taxableBase");
    }

    [Fact]
    public void Create_Rejects_Vat_Rate_With_More_Than_4_Decimals()
    {
        var act = () => PaymentAggregate.Create(Guid.NewGuid(), "2026-D2", D0, 0.20001m, 165.00m, 33.00m, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("vatRate");
    }

    [Fact]
    public void Nominal_Transmission_Cycle_Calculated_To_Transmitted()
    {
        var agg = PaymentAggregate.Create(Guid.NewGuid(), "2026-D2", D0, 0.2000m, 165.00m, 33.00m, T0);

        var sending = agg.BeginSending(T0.AddMinutes(1));
        agg.State.Should().Be(PaymentAggregateState.Sending);
        sending.EventType.Should().Be(PaymentAggregateEventType.AggregateSending);
        sending.State.Should().Be(PaymentAggregateState.Sending);
        sending.PayloadSnapshot.Should().BeNull("un simple changement d'état n'archive pas de snapshot.");

        var transmitted = agg.MarkTransmitted(Snapshots, T0.AddMinutes(2));
        agg.State.Should().Be(PaymentAggregateState.Transmitted);
        transmitted.EventType.Should().Be(PaymentAggregateEventType.AggregateTransmitted);
        transmitted.PayloadSnapshot.Should().Be(Snapshots.PayloadSnapshot);
        transmitted.PaResponseSnapshot.Should().Be(Snapshots.PaResponseSnapshot);
    }

    [Fact]
    public void Rejected_Transmission_Carries_Snapshots()
    {
        var agg = InState(PaymentAggregateState.Sending);

        var rejected = agg.MarkRejectedByPa(Snapshots, T0.AddMinutes(1), reason: "Ledger indisponible");

        agg.State.Should().Be(PaymentAggregateState.RejectedByPa);
        rejected.PayloadSnapshot.Should().Be(Snapshots.PayloadSnapshot);
        rejected.PaResponseSnapshot.Should().Be(Snapshots.PaResponseSnapshot);
        rejected.Detail.Should().Contain("Ledger indisponible");
    }

    [Fact]
    public void TechnicalError_Can_Be_Retried_To_Sending()
    {
        var agg = InState(PaymentAggregateState.TechnicalError);

        var sending = agg.BeginSending(T0.AddMinutes(1));

        agg.State.Should().Be(PaymentAggregateState.Sending);
        sending.Detail.Should().Contain("TechnicalError → Sending");
    }

    [Theory]
    [InlineData(PaymentAggregateState.Calculated, PaymentAggregateState.Transmitted)]
    [InlineData(PaymentAggregateState.Calculated, PaymentAggregateState.RejectedByPa)]
    [InlineData(PaymentAggregateState.TechnicalError, PaymentAggregateState.Transmitted)]
    public void Illegal_Transition_Is_Rejected_And_Leaves_State_Unchanged(PaymentAggregateState from, PaymentAggregateState to)
    {
        var agg = InState(from);

        var act = () => Invoke(agg, to);

        act.Should().Throw<InvalidPaymentAggregateTransitionException>();
        agg.State.Should().Be(from, "le contrôle de légalité précède toute mutation.");
    }

    [Theory]
    [InlineData(PaymentAggregateState.Transmitted)]
    [InlineData(PaymentAggregateState.RejectedByPa)]
    public void Terminal_States_Reject_Every_Transition(PaymentAggregateState sink)
    {
        foreach (var to in Enum.GetValues<PaymentAggregateState>())
        {
            if (to == PaymentAggregateState.Calculated)
            {
                continue; // aucune transition ne vise l'état initial Calculated.
            }

            var agg = InState(sink);

            var act = () => Invoke(agg, to);

            act.Should().Throw<InvalidPaymentAggregateTransitionException>($"aucune transition ne sort de {sink} (vers {to}).");
            agg.State.Should().Be(sink);
        }
    }

    [Fact]
    public void MarkTransmitted_Rejects_Null_Snapshots()
    {
        var agg = InState(PaymentAggregateState.Sending);

        var act = () => agg.MarkTransmitted(null!, T0.AddMinutes(1));

        act.Should().Throw<ArgumentNullException>();
        agg.State.Should().Be(PaymentAggregateState.Sending);
    }

    private static PaymentAggregateEvent Invoke(PaymentAggregate agg, PaymentAggregateState to) => to switch
    {
        PaymentAggregateState.Sending => agg.BeginSending(T0.AddMinutes(5)),
        PaymentAggregateState.Transmitted => agg.MarkTransmitted(Snapshots, T0.AddMinutes(5)),
        PaymentAggregateState.RejectedByPa => agg.MarkRejectedByPa(Snapshots, T0.AddMinutes(5)),
        PaymentAggregateState.TechnicalError => agg.MarkTechnicalError(T0.AddMinutes(5)),
        PaymentAggregateState.Calculated => throw new InvalidOperationException("Aucune transition ne vise Calculated."),
        _ => throw new ArgumentOutOfRangeException(nameof(to)),
    };

    private static PaymentAggregate InState(PaymentAggregateState state) => PaymentAggregate.Reconstitute(
        Guid.NewGuid(), "2026-D2", D0, 0.2000m, 165.00m, 33.00m, state, T0, T0);
}
