namespace Liakont.Modules.Payments.Domain.Entities;

using System;

/// <summary>
/// Entrée IMMUABLE de la piste d'audit d'un <see cref="PaymentAggregate"/> (F06 §3 / F09, item TRK04) — même
/// discipline que <c>DocumentEvent</c> : les agrégats de paiement transmis à la DGFiP ont la même valeur
/// fiscale que les documents. Le journal est APPEND-ONLY : aucun chemin d'update/delete applicatif, et la
/// garantie est renforcée AU NIVEAU BASE par des triggers (CLAUDE.md n°4 ; vérifié par test). Chaque
/// transmission journalise le payload envoyé, la réponse PA, l'horodatage et l'état atteint.
/// </summary>
public sealed class PaymentAggregateEvent
{
    private PaymentAggregateEvent()
    {
    }

    /// <summary>Identifiant de l'entrée d'audit.</summary>
    public Guid Id { get; private set; }

    /// <summary>Agrégat concerné.</summary>
    public Guid AggregateId { get; private set; }

    /// <summary>Horodatage de l'événement (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; private set; }

    /// <summary>Type d'événement (F06 §3 / F09).</summary>
    public PaymentAggregateEventType EventType { get; private set; }

    /// <summary>État de l'agrégat APRÈS l'événement (genèse = <c>Calculated</c>, puis outcome de transmission).</summary>
    public PaymentAggregateState State { get; private set; }

    /// <summary>Détail textuel (message d'audit, lisible).</summary>
    public string? Detail { get; private set; }

    /// <summary>Snapshot du payload d'agrégat transmis (JSON), pour une transmission (acceptée ou rejetée).</summary>
    public string? PayloadSnapshot { get; private set; }

    /// <summary>Réponse brute de la Plateforme Agréée (JSON), pour une transmission.</summary>
    public string? PaResponseSnapshot { get; private set; }

    /// <summary>
    /// Crée l'événement de GENÈSE écrit à la création de l'agrégat par le pipeline (PIP03) en état
    /// <see cref="PaymentAggregateState.Calculated"/>. Sans snapshot (l'agrégat n'est pas encore transmis).
    /// </summary>
    public static PaymentAggregateEvent Genesis(Guid aggregateId, DateTimeOffset occurredAtUtc)
    {
        return new PaymentAggregateEvent
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            TimestampUtc = occurredAtUtc,
            EventType = PaymentAggregateEventType.AggregateCalculated,
            State = PaymentAggregateState.Calculated,
            Detail = "Agrégat de paiement calculé par le pipeline (état Calculated).",
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
        };
    }

    /// <summary>
    /// Crée l'événement d'audit d'une TRANSITION de transmission, produit AUTOMATIQUEMENT par chaque transition
    /// de l'agrégat : une transition ne peut pas survenir sans son fait d'audit. Les snapshots
    /// (<paramref name="payloadSnapshot"/> / <paramref name="paResponseSnapshot"/>) portent la preuve d'une
    /// transmission (acceptée ou rejetée) ; ils sont <c>null</c> pour un simple changement d'état (Sending,
    /// erreur technique sans réponse PA).
    /// </summary>
    public static PaymentAggregateEvent Transition(
        Guid aggregateId,
        PaymentAggregateEventType eventType,
        PaymentAggregateState state,
        DateTimeOffset occurredAtUtc,
        string detail,
        string? payloadSnapshot,
        string? paResponseSnapshot)
    {
        return new PaymentAggregateEvent
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            TimestampUtc = occurredAtUtc,
            EventType = eventType,
            State = state,
            Detail = detail,
            PayloadSnapshot = payloadSnapshot,
            PaResponseSnapshot = paResponseSnapshot,
        };
    }

    /// <summary>Reconstitue une entrée d'audit depuis la persistance (lecture).</summary>
    public static PaymentAggregateEvent Reconstitute(
        Guid id,
        Guid aggregateId,
        DateTimeOffset timestampUtc,
        PaymentAggregateEventType eventType,
        PaymentAggregateState state,
        string? detail,
        string? payloadSnapshot,
        string? paResponseSnapshot)
    {
        return new PaymentAggregateEvent
        {
            Id = id,
            AggregateId = aggregateId,
            TimestampUtc = timestampUtc,
            EventType = eventType,
            State = state,
            Detail = detail,
            PayloadSnapshot = payloadSnapshot,
            PaResponseSnapshot = paResponseSnapshot,
        };
    }
}
