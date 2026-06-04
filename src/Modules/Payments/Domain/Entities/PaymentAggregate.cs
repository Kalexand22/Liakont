namespace Liakont.Modules.Payments.Domain.Entities;

using System;
using Liakont.Modules.Payments.Domain.StateMachine;

/// <summary>
/// Agrégat jour × taux de l'e-reporting de paiement (F09 §5.3 / F06 §3, item TRK04). Il porte, pour une
/// <see cref="Period"/> déclarative et un jour d'encaissement (<see cref="AggregateDate"/>), la base taxable
/// et la TVA encaissées ventilées par <see cref="VatRate"/> — données transmises à la DGFiP (SIREN + période
/// + montants par jour et par taux, F09 §2), sans aucune donnée nominative. TRK04 porte le MODÈLE et la
/// PERSISTANCE : le CALCUL d'agrégation (somme des paiements par jour et taux) arrive avec le pipeline
/// (PIP03) ; ce module ne calcule rien. Vit dans la base DU TENANT (database-per-tenant) ; rétention 10 ans,
/// jamais purgé (F06 §6).
/// </summary>
/// <remarks>
/// Les montants sont en <see cref="decimal"/> (CLAUDE.md n°1) et peuvent être NÉGATIFS (trop-perçu /
/// remboursement via rectificatif — F09 §5.4). Chaque transition de transmission PRODUIT son
/// <see cref="PaymentAggregateEvent"/> d'audit (on ne transmet jamais sans fait d'audit), persisté
/// atomiquement avec l'état. L'unité de <see cref="VatRate"/> (taux) est celle fixée par le pipeline (PIP03)
/// — TRK04 la stocke fidèlement sans l'interpréter.
/// </remarks>
public sealed class PaymentAggregate
{
    private PaymentAggregate()
    {
    }

    /// <summary>Identifiant de l'agrégat.</summary>
    public Guid Id { get; private set; }

    /// <summary>Période déclarative de rattachement (libellé porté par le paramétrage du tenant — F09 §2).</summary>
    public string Period { get; private set; } = string.Empty;

    /// <summary>Jour d'encaissement agrégé (l'e-reporting de paiement est agrégé par jour — F09 §2).</summary>
    public DateOnly AggregateDate { get; private set; }

    /// <summary>Taux de TVA de la ventilation (decimal), tel que calculé par le pipeline (PIP03).</summary>
    public decimal VatRate { get; private set; }

    /// <summary>Base taxable encaissée du jour pour ce taux (decimal, peut être négative — F09 §5.4).</summary>
    public decimal TaxableBase { get; private set; }

    /// <summary>TVA encaissée du jour pour ce taux (decimal, peut être négative — F09 §5.4).</summary>
    public decimal VatAmount { get; private set; }

    /// <summary>État de transmission de l'agrégat (F09 / F06 §3).</summary>
    public PaymentAggregateState State { get; private set; }

    /// <summary>Création de l'agrégat (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>Dernière mise à jour de l'agrégat (UTC).</summary>
    public DateTimeOffset LastUpdateUtc { get; private set; }

    /// <summary>
    /// Crée un agrégat en état <see cref="PaymentAggregateState.Calculated"/> à partir des montants CALCULÉS
    /// par le pipeline (PIP03). Les montants sont conservés tels que calculés (le module ne recalcule rien).
    /// </summary>
    public static PaymentAggregate Create(
        Guid id,
        string period,
        DateOnly aggregateDate,
        decimal vatRate,
        decimal taxableBase,
        decimal vatAmount,
        DateTimeOffset createdUtc)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            throw new ArgumentException("La période déclarative de l'agrégat est obligatoire (F09 §2).", nameof(period));
        }

        MonetaryScale.RequireRate(vatRate, nameof(vatRate));
        MonetaryScale.RequireAmount(taxableBase, nameof(taxableBase));
        MonetaryScale.RequireAmount(vatAmount, nameof(vatAmount));

        return new PaymentAggregate
        {
            Id = id,
            Period = period.Trim(),
            AggregateDate = aggregateDate,
            VatRate = vatRate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
            State = PaymentAggregateState.Calculated,
            CreatedUtc = createdUtc,
            LastUpdateUtc = createdUtc,
        };
    }

    /// <summary>Reconstitue un agrégat depuis la persistance (lecture).</summary>
    public static PaymentAggregate Reconstitute(
        Guid id,
        string period,
        DateOnly aggregateDate,
        decimal vatRate,
        decimal taxableBase,
        decimal vatAmount,
        PaymentAggregateState state,
        DateTimeOffset createdUtc,
        DateTimeOffset lastUpdateUtc)
    {
        return new PaymentAggregate
        {
            Id = id,
            Period = period,
            AggregateDate = aggregateDate,
            VatRate = vatRate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
            State = state,
            CreatedUtc = createdUtc,
            LastUpdateUtc = lastUpdateUtc,
        };
    }

    // ── Cycle de transmission (item TRK04) ────────────────────────────────────────────────────────────
    // Chaque transition VALIDE sa légalité via PaymentAggregateStateMachine (refus AVANT toute mutation),
    // change l'état, avance LastUpdateUtc, et RETOURNE le PaymentAggregateEvent qui la matérialise — persisté
    // dans la MÊME transaction que l'état (atomicité, F06 §3).

    /// <summary>Calculated/TechnicalError → Sending : la transmission de l'agrégat à la Plateforme Agréée est engagée.</summary>
    public PaymentAggregateEvent BeginSending(DateTimeOffset occurredAtUtc, string? detail = null)
        => ApplyTransition(PaymentAggregateState.Sending, PaymentAggregateEventType.AggregateSending, occurredAtUtc, detail, payloadSnapshot: null, paResponseSnapshot: null);

    /// <summary>
    /// Sending → Transmitted : agrégat accepté par la Plateforme Agréée. Les <paramref name="snapshots"/>
    /// (payload transmis + réponse PA brute) sont OBLIGATOIRES et archivés dans l'événement d'audit (F06 §3).
    /// </summary>
    public PaymentAggregateEvent MarkTransmitted(AggregateTransmissionSnapshots snapshots, DateTimeOffset occurredAtUtc, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return ApplyTransition(
            PaymentAggregateState.Transmitted,
            PaymentAggregateEventType.AggregateTransmitted,
            occurredAtUtc,
            detail,
            snapshots.PayloadSnapshot,
            snapshots.PaResponseSnapshot);
    }

    /// <summary>
    /// Sending → RejectedByPa : agrégat rejeté par la Plateforme Agréée. Les <paramref name="snapshots"/>
    /// (payload transmis + réponse de rejet brute) sont OBLIGATOIRES et archivés (la tentative ratée fait
    /// partie de la piste d'audit, F06 §3). <paramref name="reason"/> = motif lisible (optionnel).
    /// </summary>
    public PaymentAggregateEvent MarkRejectedByPa(AggregateTransmissionSnapshots snapshots, DateTimeOffset occurredAtUtc, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return ApplyTransition(
            PaymentAggregateState.RejectedByPa,
            PaymentAggregateEventType.AggregateRejectedByPa,
            occurredAtUtc,
            reason,
            snapshots.PayloadSnapshot,
            snapshots.PaResponseSnapshot);
    }

    /// <summary>Sending → TechnicalError : erreur technique de transmission, re-tentable (TechnicalError → Sending au prochain traitement).</summary>
    public PaymentAggregateEvent MarkTechnicalError(DateTimeOffset occurredAtUtc, string? detail = null)
        => ApplyTransition(PaymentAggregateState.TechnicalError, PaymentAggregateEventType.AggregateTechnicalError, occurredAtUtc, detail, payloadSnapshot: null, paResponseSnapshot: null);

    private PaymentAggregateEvent ApplyTransition(
        PaymentAggregateState target,
        PaymentAggregateEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? detail,
        string? payloadSnapshot,
        string? paResponseSnapshot)
    {
        var from = State;
        PaymentAggregateStateMachine.EnsureCanTransition(from, target);

        State = target;
        LastUpdateUtc = occurredAtUtc;

        var auditDetail = string.IsNullOrWhiteSpace(detail)
            ? $"Transition {from} → {target}."
            : $"Transition {from} → {target}. {detail.Trim()}";

        return PaymentAggregateEvent.Transition(Id, eventType, target, occurredAtUtc, auditDetail, payloadSnapshot, paResponseSnapshot);
    }
}
