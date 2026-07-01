namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Domain.StateMachine;
using Xunit;

/// <summary>
/// Machine à états EXPLICITE du document (F06 §3, item TRK02 — INV-DOCUMENTS-009). La table des transitions
/// est la source de vérité unique de la légalité : on vérifie qu'elle autorise EXACTEMENT les transitions de
/// la spec (ni plus, ni moins), que les états sans suite (Issued, EReported + terminaux Superseded/ManuallyHandled)
/// n'ont aucune sortie, et que le contrôle précède toute mutation.
/// </summary>
public sealed class DocumentStateMachineTests
{
    private static readonly HashSet<(DocumentState From, DocumentState To)> ExpectedAllowed = new()
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
        (DocumentState.RejectedByPa, DocumentState.ReadyToSend),
        (DocumentState.RejectedByPa, DocumentState.Blocked),

        // Voie e-reporting B2C agrégée (BUG-24, ADR-0037) : un document validé aboutit à EReported.
        (DocumentState.ReadyToSend, DocumentState.EReported),
    };

    [Fact]
    public void Allows_Exactly_The_Specified_Transitions_And_No_Other()
    {
        foreach (var from in Enum.GetValues<DocumentState>())
        {
            foreach (var to in Enum.GetValues<DocumentState>())
            {
                var expected = ExpectedAllowed.Contains((from, to));
                DocumentStateMachine.IsAllowed(from, to).Should().Be(
                    expected,
                    $"la transition {from} → {to} doit être {(expected ? "autorisée" : "refusée")} (F06 §3).");
            }
        }
    }

    [Theory]
    [InlineData(DocumentState.Issued)]
    [InlineData(DocumentState.EReported)]
    [InlineData(DocumentState.Superseded)]
    [InlineData(DocumentState.ManuallyHandled)]
    public void States_Without_Successor_Have_No_Outgoing_Transition(DocumentState sink)
    {
        foreach (var to in Enum.GetValues<DocumentState>())
        {
            DocumentStateMachine.IsAllowed(sink, to).Should().BeFalse(
                $"{sink} n'a aucune transition sortante (état d'émission réussie ou terminal — F06 §3).");
        }
    }

    [Fact]
    public void EnsureCanTransition_Throws_On_An_Illegal_Transition()
    {
        var act = () => DocumentStateMachine.EnsureCanTransition(DocumentState.Detected, DocumentState.Issued);

        act.Should().Throw<InvalidDocumentTransitionException>()
            .Where(e => e.From == DocumentState.Detected && e.To == DocumentState.Issued);
    }

    [Fact]
    public void EnsureCanTransition_Does_Not_Throw_On_A_Legal_Transition()
    {
        var act = () => DocumentStateMachine.EnsureCanTransition(DocumentState.Detected, DocumentState.ReadyToSend);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(DocumentState.ReadyToSend)]
    [InlineData(DocumentState.Blocked)]
    public void RejectedByPa_Can_Be_ReVerified_To_ReadyToSend_Or_Blocked(DocumentState target)
    {
        // Re-vérification d'un document rejeté par la PA après correction : ReadyToSend (cause corrigée) ou Blocked
        // (cause non corrigée — le document quitte le cul-de-sac pour montrer le motif). « Bloquer plutôt qu'envoyer
        // faux » (CLAUDE.md n°3).
        DocumentStateMachine.IsAllowed(DocumentState.RejectedByPa, target).Should().BeTrue(
            $"la re-vérification autorise RejectedByPa → {target} (F06 §3).");
    }

    [Fact]
    public void RejectedByPa_Cannot_Reach_Sending_Or_Issued_Directly()
    {
        // La re-vérification ne court-circuite jamais le flux d'envoi : un rejeté repasse par ReadyToSend/Blocked,
        // jamais directement Sending/Issued (qui resteraient des transitions illégales).
        DocumentStateMachine.IsAllowed(DocumentState.RejectedByPa, DocumentState.Sending).Should().BeFalse();
        DocumentStateMachine.IsAllowed(DocumentState.RejectedByPa, DocumentState.Issued).Should().BeFalse();
    }

    [Fact]
    public void EReported_Is_Reachable_Only_From_ReadyToSend()
    {
        // Voie e-reporting B2C AGRÉGÉE (BUG-24, ADR-0037) : un document validé (ReadyToSend) aboutit à EReported au
        // hook d'émission agrégée. C'est le SEUL chemin vers EReported — jamais via Sending/Issued (voie document).
        DocumentStateMachine.IsAllowed(DocumentState.ReadyToSend, DocumentState.EReported).Should().BeTrue();

        foreach (var from in Enum.GetValues<DocumentState>())
        {
            if (from == DocumentState.ReadyToSend)
            {
                continue;
            }

            DocumentStateMachine.IsAllowed(from, DocumentState.EReported).Should().BeFalse(
                $"EReported n'est atteignable que depuis ReadyToSend (BUG-24/ADR-0037), pas depuis {from}.");
        }
    }
}
