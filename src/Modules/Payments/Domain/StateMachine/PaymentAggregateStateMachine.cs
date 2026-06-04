namespace Liakont.Modules.Payments.Domain.StateMachine;

using System.Collections.Generic;
using Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// Machine à états EXPLICITE de la transmission d'un agrégat de paiement (F09 / F06 §3, item TRK04) : la
/// liste FERMÉE des transitions autorisées, source de vérité unique de la légalité d'un changement d'état de
/// transmission. Toute transition hors de cette liste est refusée
/// (<see cref="InvalidPaymentAggregateTransitionException"/>) — un agrégat transmis à la DGFiP est une
/// donnée d'audit fiscal qui ne change jamais par un chemin non modélisé (même discipline que
/// <c>DocumentStateMachine</c>).
/// </summary>
/// <remarks>
/// Transitions autorisées (cycle de transmission, opérationnel — pas une règle fiscale) :
/// <list type="bullet">
///   <item><c>Calculated</c> → <c>Sending</c></item>
///   <item><c>Sending</c> → <c>Transmitted</c> | <c>RejectedByPa</c> | <c>TechnicalError</c></item>
///   <item><c>TechnicalError</c> → <c>Sending</c> (re-tentable au prochain traitement)</item>
/// </list>
/// <c>Transmitted</c> et <c>RejectedByPa</c> n'ont AUCUNE transition sortante (états terminaux : un agrégat
/// rejeté est remplacé par un nouvel agrégat recalculé par le pipeline, jamais muté en place).
/// </remarks>
public static class PaymentAggregateStateMachine
{
    private static readonly HashSet<(PaymentAggregateState From, PaymentAggregateState To)> AllowedTransitions = new()
    {
        (PaymentAggregateState.Calculated, PaymentAggregateState.Sending),
        (PaymentAggregateState.Sending, PaymentAggregateState.Transmitted),
        (PaymentAggregateState.Sending, PaymentAggregateState.RejectedByPa),
        (PaymentAggregateState.Sending, PaymentAggregateState.TechnicalError),
        (PaymentAggregateState.TechnicalError, PaymentAggregateState.Sending),
    };

    /// <summary>Indique si la transition <paramref name="from"/> → <paramref name="to"/> est autorisée.</summary>
    public static bool IsAllowed(PaymentAggregateState from, PaymentAggregateState to)
    {
        return AllowedTransitions.Contains((from, to));
    }

    /// <summary>
    /// Vérifie que la transition <paramref name="from"/> → <paramref name="to"/> est autorisée, sinon lève
    /// <see cref="InvalidPaymentAggregateTransitionException"/>. Ne mute aucun état : le contrôle précède
    /// l'écriture (un état refusé n'altère jamais l'agrégat).
    /// </summary>
    public static void EnsureCanTransition(PaymentAggregateState from, PaymentAggregateState to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidPaymentAggregateTransitionException(from, to);
        }
    }
}
