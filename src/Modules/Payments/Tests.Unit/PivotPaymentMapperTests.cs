namespace Liakont.Modules.Payments.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Payments.Infrastructure;
using Xunit;

/// <summary>
/// Projection <see cref="PivotPaymentMapper"/> (TRK04) : report fidèle de l'encaissement brut reçu de l'agent
/// vers <c>Payment</c>, sans interprétation fiscale ; la date d'encaissement est ramenée au jour (F09 §2).
/// </summary>
public sealed class PivotPaymentMapperTests
{
    private static readonly DateTimeOffset Received = new(2026, 5, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToPayment_Reports_Source_Values_And_Reduces_Date_To_Day()
    {
        var id = Guid.NewGuid();
        var pivot = new PivotPaymentDto(
            paymentDate: new DateTime(2026, 5, 14, 10, 30, 0, DateTimeKind.Utc),
            amount: 1162.80m,
            method: "CB",
            relatedDocumentNumber: "F-2026-001",
            sourceReference: "ENC-1");

        var payment = PivotPaymentMapper.ToPayment(pivot, id, Received);

        payment.Id.Should().Be(id);
        payment.PaymentDate.Should().Be(new DateOnly(2026, 5, 14), "l'e-reporting de paiement est agrégé par jour (F09 §2).");
        payment.Amount.Should().Be(1162.80m);
        payment.Method.Should().Be("CB");
        payment.RelatedDocumentNumber.Should().Be("F-2026-001");
        payment.SourceReference.Should().Be("ENC-1");
        payment.ReceivedUtc.Should().Be(Received);
    }

    [Fact]
    public void ToPayment_Keeps_Optional_Fields_Null_When_Absent()
    {
        var pivot = new PivotPaymentDto(
            paymentDate: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            amount: 50.00m);

        var payment = PivotPaymentMapper.ToPayment(pivot, Guid.NewGuid(), Received);

        payment.Method.Should().BeNull();
        payment.RelatedDocumentNumber.Should().BeNull();
        payment.SourceReference.Should().BeNull();
    }
}
