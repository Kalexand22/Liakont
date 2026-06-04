namespace Liakont.Modules.Documents.Domain.StateMachine;

using System;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Levée quand une transition d'état NON autorisée par la machine à états du document (F06 §3, item TRK02)
/// est tentée — y compris toute transition au départ d'un état TERMINAL (<see cref="DocumentState.Issued"/>,
/// <see cref="DocumentState.Superseded"/>, <see cref="DocumentState.ManuallyHandled"/>). C'est un garde-fou
/// d'invariant : un état de document est une donnée d'audit fiscal, il ne change jamais par un chemin non
/// modélisé (CLAUDE.md n°3 — bloquer plutôt qu'avancer un état faux).
/// </summary>
public sealed class InvalidDocumentTransitionException : InvalidOperationException
{
    public InvalidDocumentTransitionException(DocumentState from, DocumentState to)
        : base($"Transition d'état interdite : {from} → {to} n'est pas autorisée par la machine à états du document (F06 §3).")
    {
        From = from;
        To = to;
    }

    /// <summary>État de provenance de la transition refusée.</summary>
    public DocumentState From { get; }

    /// <summary>État cible de la transition refusée.</summary>
    public DocumentState To { get; }
}
