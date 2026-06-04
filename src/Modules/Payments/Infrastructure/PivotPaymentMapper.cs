namespace Liakont.Modules.Payments.Infrastructure;

using System;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// Projette un encaissement reçu de l'agent (<see cref="PivotPaymentDto"/>, contrat agent) vers l'agrégat
/// <see cref="Payment"/> (item TRK04). Mapping PUR (sans état, sans I/O) : il REPORTE le montant calculé par
/// la source, sans interprétation fiscale (l'agrégation jour × taux est portée par le pipeline, PIP03). La
/// date d'encaissement est ramenée au jour (l'e-reporting de paiement est agrégé par jour — F09 §2). Fonction
/// isolée pour être testée en unitaire.
/// </summary>
internal static class PivotPaymentMapper
{
    public static Payment ToPayment(PivotPaymentDto pivot, Guid id, DateTimeOffset receivedUtc)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        return Payment.Create(
            id: id,
            paymentDate: DateOnly.FromDateTime(pivot.PaymentDate),
            amount: pivot.Amount,
            method: pivot.Method,
            relatedDocumentNumber: pivot.RelatedDocumentNumber,
            sourceReference: pivot.SourceReference,
            receivedUtc: receivedUtc);
    }
}
