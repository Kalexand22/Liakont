namespace Liakont.Modules.Payments.Tests.Integration;

using System;
using Liakont.Modules.Payments.Domain.Entities;

/// <summary>Fabriques de données de test du module Payments (TRK04) — valeurs par défaut réalistes, surchargées au besoin.</summary>
internal static class PaymentTestData
{
    public static readonly DateTimeOffset ReceivedAt = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Day = new(2026, 5, 14);

    public static Payment NewPayment(decimal amount = 1162.80m, string? sourceReference = null)
        => Payment.Create(
            Guid.NewGuid(),
            Day,
            amount,
            method: "CB",
            relatedDocumentNumber: "F-2026-001",
            sourceReference: sourceReference ?? $"ENC-{Guid.NewGuid():N}",
            receivedUtc: ReceivedAt);

    public static PaymentAggregate NewAggregate(decimal taxableBase = 165.00m, decimal vatAmount = 33.00m, decimal vatRate = 0.2000m)
        => PaymentAggregate.Create(
            Guid.NewGuid(),
            period: "2026-D2",
            aggregateDate: Day,
            vatRate: vatRate,
            taxableBase: taxableBase,
            vatAmount: vatAmount,
            createdUtc: ReceivedAt);
}
