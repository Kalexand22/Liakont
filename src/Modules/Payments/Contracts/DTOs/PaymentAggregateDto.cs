namespace Liakont.Modules.Payments.Contracts.DTOs;

using System;

/// <summary>
/// Agrégat jour × taux de l'e-reporting de paiement exposé en lecture (F09 §5.3, item TRK04). Montants en
/// <see cref="decimal"/> (CLAUDE.md n°1) ; <see cref="State"/> est le libellé textuel de l'état de transmission.
/// </summary>
public sealed record PaymentAggregateDto
{
    public required Guid Id { get; init; }

    public required string Period { get; init; }

    public required DateOnly AggregateDate { get; init; }

    public required decimal VatRate { get; init; }

    public required decimal TaxableBase { get; init; }

    public required decimal VatAmount { get; init; }

    public required string State { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required DateTimeOffset LastUpdateUtc { get; init; }
}
