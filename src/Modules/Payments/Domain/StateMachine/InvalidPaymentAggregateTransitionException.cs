namespace Liakont.Modules.Payments.Domain.StateMachine;

using System;
using Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// Levée lorsqu'une transition d'état d'un <see cref="PaymentAggregate"/> sort de la table fermée des
/// transitions autorisées (<see cref="PaymentAggregateStateMachine"/>, item TRK04). Le contrôle précède
/// toute mutation : un état refusé n'altère jamais l'agrégat (CLAUDE.md n°3 — bloquer plutôt qu'avancer
/// un état faux).
/// </summary>
public sealed class InvalidPaymentAggregateTransitionException : Exception
{
    /// <summary>Crée l'exception pour une transition <paramref name="from"/> → <paramref name="to"/> non autorisée.</summary>
    public InvalidPaymentAggregateTransitionException(PaymentAggregateState from, PaymentAggregateState to)
        : base($"Transition d'agrégat de paiement interdite : {from} → {to} ne fait pas partie des transitions autorisées (F09 / TRK04).")
    {
        From = from;
        To = to;
    }

    /// <summary>État de provenance refusé.</summary>
    public PaymentAggregateState From { get; }

    /// <summary>État cible refusé.</summary>
    public PaymentAggregateState To { get; }
}
