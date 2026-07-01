namespace Liakont.Modules.Documents.Domain.StateMachine;

using System.Collections.Generic;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Machine à états EXPLICITE du document (F06 §3, item TRK02) : la liste FERMÉE des transitions autorisées,
/// source de vérité unique de la légalité d'un changement d'état. Toute transition hors de cette liste est
/// refusée (<see cref="InvalidDocumentTransitionException"/>) — un état de document est une donnée d'audit
/// fiscal qui ne change jamais par un chemin non modélisé.
/// </summary>
/// <remarks>
/// Transitions autorisées (F06 §3 / TRK02) :
/// <list type="bullet">
///   <item><c>Detected</c> → <c>Blocked</c> | <c>ReadyToSend</c></item>
///   <item><c>Blocked</c> → <c>ReadyToSend</c> (après correction source) | <c>ManuallyHandled</c> (action opérateur)</item>
///   <item><c>ReadyToSend</c> → <c>Sending</c></item>
///   <item><c>Sending</c> → <c>Issued</c> | <c>RejectedByPa</c> | <c>TechnicalError</c></item>
///   <item><c>TechnicalError</c> → <c>ReadyToSend</c> (re-tentable au prochain traitement)</item>
///   <item><c>RejectedByPa</c> → <c>Superseded</c> (remplacé après rejet) | <c>ManuallyHandled</c> (action opérateur) | <c>ReadyToSend</c> (re-vérifié après correction) | <c>Blocked</c> (re-vérifié, cause non corrigée)</item>
///   <item><c>ReadyToSend</c> → <c>EReported</c> (voie e-reporting B2C AGRÉGÉE acceptée par la PA — BUG-24/ADR-0037)</item>
/// </list>
/// <c>Issued</c>, <c>EReported</c>, <c>Superseded</c> et <c>ManuallyHandled</c> n'ont AUCUNE transition sortante :
/// ce sont des états sans suite (les deux derniers sont les états terminaux déclarés, F06 §3 ; <c>Issued</c> est
/// l'état d'émission réussie de la voie document ; <c>EReported</c> celui de la voie e-reporting B2C agrégée — un
/// document abouti n'est plus déplacé par la machine, l'anti-doublon et la détection d'altération après émission
/// de TRK03 opèrent par ÉVÉNEMENTS, jamais par transition d'état).
/// </remarks>
public static class DocumentStateMachine
{
    private static readonly HashSet<(DocumentState From, DocumentState To)> AllowedTransitions = new()
    {
        (DocumentState.Detected, DocumentState.Blocked),
        (DocumentState.Detected, DocumentState.ReadyToSend),
        (DocumentState.Blocked, DocumentState.ReadyToSend),
        (DocumentState.Blocked, DocumentState.ManuallyHandled),
        (DocumentState.ReadyToSend, DocumentState.Sending),
        (DocumentState.Sending, DocumentState.Issued),
        (DocumentState.Sending, DocumentState.RejectedByPa),
        (DocumentState.Sending, DocumentState.TechnicalError),
        (DocumentState.TechnicalError, DocumentState.ReadyToSend),
        (DocumentState.RejectedByPa, DocumentState.Superseded),
        (DocumentState.RejectedByPa, DocumentState.ManuallyHandled),

        // Re-vérification d'un document rejeté par la PA après correction de la cause (mentions B2B saisies,
        // mapping complété) : soit la cause est corrigée et le document repart ReadyToSend, soit elle ne l'est
        // pas et le document quitte l'état rejeté pour Blocked, qui montre le motif réévalué à corriger
        // (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3). Réutilise le mécanisme de re-vérification (FIX02).
        (DocumentState.RejectedByPa, DocumentState.ReadyToSend),
        (DocumentState.RejectedByPa, DocumentState.Blocked),

        // Voie e-reporting B2C AGRÉGÉE (BUG-24, ADR-0037) : un document validé (ReadyToSend) inclus dans une
        // déclaration agrégée (jour × devise × taux) ACCEPTÉE par la PA aboutit à EReported. Il ne passe JAMAIS
        // par Sending/Issued (réservés à la voie document, transmission pièce-à-pièce). EReported est sans
        // transition sortante (comme Issued). Déclenché au hook d'émission unique B2cReportingEmitter.EmitOneAsync,
        // par contribution, après confirmation d'envoi.
        (DocumentState.ReadyToSend, DocumentState.EReported),
    };

    /// <summary>Indique si la transition <paramref name="from"/> → <paramref name="to"/> est autorisée.</summary>
    public static bool IsAllowed(DocumentState from, DocumentState to)
    {
        return AllowedTransitions.Contains((from, to));
    }

    /// <summary>
    /// Vérifie que la transition <paramref name="from"/> → <paramref name="to"/> est autorisée, sinon lève
    /// <see cref="InvalidDocumentTransitionException"/>. Ne mute aucun état : le contrôle précède l'écriture
    /// (un état refusé n'altère jamais l'agrégat).
    /// </summary>
    public static void EnsureCanTransition(DocumentState from, DocumentState to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidDocumentTransitionException(from, to);
        }
    }
}
