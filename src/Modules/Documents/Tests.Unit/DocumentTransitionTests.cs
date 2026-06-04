namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Domain.StateMachine;
using Xunit;

/// <summary>
/// Machine à états de l'agrégat <see cref="Document"/> (F06 §3, item TRK02 — INV-DOCUMENTS-009/010) :
/// cycle nominal, rejet des transitions illégales, reprise <c>TechnicalError</c>, traitement manuel (motif
/// obligatoire) et remplacement après rejet (lien remplaçant). Chaque transition PRODUIT son
/// <see cref="DocumentEvent"/> et avance <c>LastUpdateUtc</c> ; une transition refusée n'altère jamais l'état.
/// </summary>
public sealed class DocumentTransitionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nominal_Cycle_Detected_To_Issued_Writes_One_Event_Per_Transition()
    {
        var doc = NewDetected();

        var ready = doc.MarkReadyToSend(T0.AddMinutes(1));
        doc.State.Should().Be(DocumentState.ReadyToSend);
        doc.LastUpdateUtc.Should().Be(T0.AddMinutes(1));
        ready.EventType.Should().Be(DocumentEventType.DocumentReadyToSend);
        ready.DocumentId.Should().Be(doc.Id);
        ready.TimestampUtc.Should().Be(T0.AddMinutes(1));
        ready.OperatorIdentity.Should().BeNull("une transition du pipeline est un événement système.");
        ready.Detail.Should().Contain("Detected → ReadyToSend");

        var sending = doc.BeginSending(T0.AddMinutes(2));
        doc.State.Should().Be(DocumentState.Sending);
        sending.EventType.Should().Be(DocumentEventType.DocumentSending);

        var issued = doc.MarkIssued(T0.AddMinutes(3));
        doc.State.Should().Be(DocumentState.Issued);
        doc.LastUpdateUtc.Should().Be(T0.AddMinutes(3));
        issued.EventType.Should().Be(DocumentEventType.DocumentIssued);
    }

    [Fact]
    public void Detected_Can_Be_Blocked_With_Reason_Then_Made_ReadyToSend()
    {
        var doc = NewDetected();

        var blocked = doc.MarkBlocked(T0.AddMinutes(1), reason: "Régime TVA non mappé");
        doc.State.Should().Be(DocumentState.Blocked);
        blocked.EventType.Should().Be(DocumentEventType.DocumentBlocked);
        blocked.Detail.Should().Contain("Régime TVA non mappé");

        var ready = doc.MarkReadyToSend(T0.AddMinutes(2));
        doc.State.Should().Be(DocumentState.ReadyToSend);
        ready.Detail.Should().Contain("Blocked → ReadyToSend");
    }

    [Theory]
    [InlineData(DocumentState.Detected, DocumentState.Sending)]
    [InlineData(DocumentState.Detected, DocumentState.Issued)]
    [InlineData(DocumentState.Detected, DocumentState.Superseded)]
    [InlineData(DocumentState.Detected, DocumentState.ManuallyHandled)]
    [InlineData(DocumentState.ReadyToSend, DocumentState.Issued)]
    [InlineData(DocumentState.Blocked, DocumentState.Sending)]
    [InlineData(DocumentState.Sending, DocumentState.ReadyToSend)]
    public void Illegal_Transition_Is_Rejected_And_Leaves_State_Unchanged(DocumentState from, DocumentState to)
    {
        var doc = InState(from);

        var act = () => Invoke(doc, to);

        act.Should().Throw<InvalidDocumentTransitionException>();
        doc.State.Should().Be(from, "le contrôle de légalité précède toute mutation (état inchangé sur refus).");
    }

    [Fact]
    public void TechnicalError_Can_Be_Retried_To_ReadyToSend()
    {
        var doc = InState(DocumentState.TechnicalError);

        var ready = doc.MarkReadyToSend(T0.AddMinutes(1));

        doc.State.Should().Be(DocumentState.ReadyToSend);
        ready.Detail.Should().Contain("TechnicalError → ReadyToSend");
    }

    [Fact]
    public void Blocked_To_ManuallyHandled_Records_Reason_And_Operator_Identity()
    {
        var doc = InState(DocumentState.Blocked);

        var evt = doc.MarkManuallyHandled(
            reason: "Avoir orphelin traité dans le logiciel source",
            operatorIdentity: "alice@cmp",
            occurredAtUtc: T0.AddMinutes(1));

        doc.State.Should().Be(DocumentState.ManuallyHandled);
        evt.EventType.Should().Be(DocumentEventType.DocumentManuallyHandled);
        evt.OperatorIdentity.Should().Be("alice@cmp");
        evt.Detail.Should().Contain("Avoir orphelin traité dans le logiciel source");
    }

    [Fact]
    public void RejectedByPa_Can_Also_Be_Manually_Handled()
    {
        var doc = InState(DocumentState.RejectedByPa);

        doc.MarkManuallyHandled("Document non transmissible", "bob@cmp", T0.AddMinutes(1));

        doc.State.Should().Be(DocumentState.ManuallyHandled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ManuallyHandled_Requires_A_Reason(string blank)
    {
        var doc = InState(DocumentState.Blocked);

        var act = () => doc.MarkManuallyHandled(blank, "op", T0);

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
        doc.State.Should().Be(DocumentState.Blocked, "motif manquant : l'état n'a pas changé.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ManuallyHandled_Requires_An_Operator_Identity(string blank)
    {
        var doc = InState(DocumentState.Blocked);

        var act = () => doc.MarkManuallyHandled("motif valable", blank, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("operatorIdentity");
    }

    [Fact]
    public void RejectedByPa_To_Superseded_Records_Replacement_Reference_And_Operator()
    {
        var doc = InState(DocumentState.RejectedByPa);

        var evt = doc.Supersede(
            replacementReference: "F-2026-002",
            operatorIdentity: "carol@cmp",
            occurredAtUtc: T0.AddMinutes(1));

        doc.State.Should().Be(DocumentState.Superseded);
        evt.EventType.Should().Be(DocumentEventType.DocumentSuperseded);
        evt.OperatorIdentity.Should().Be("carol@cmp");
        evt.Detail.Should().Contain("F-2026-002");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Supersede_Requires_A_Replacement_Reference(string blank)
    {
        var doc = InState(DocumentState.RejectedByPa);

        var act = () => doc.Supersede(blank, "op", T0);

        act.Should().Throw<ArgumentException>().WithParameterName("replacementReference");
        doc.State.Should().Be(DocumentState.RejectedByPa, "lien remplaçant manquant : l'état n'a pas changé.");
    }

    [Theory]
    [InlineData(DocumentState.Issued)]
    [InlineData(DocumentState.Superseded)]
    [InlineData(DocumentState.ManuallyHandled)]
    public void States_Without_Successor_Reject_Every_Transition(DocumentState sink)
    {
        // Même avec des arguments valides, aucune transition ne sort d'un état d'émission réussie ou terminal.
        foreach (var to in Enum.GetValues<DocumentState>())
        {
            if (to == DocumentState.Detected)
            {
                continue; // aucune transition ne vise l'état initial Detected (pas de méthode de transition).
            }

            var doc = InState(sink);

            var act = () => Invoke(doc, to);

            act.Should().Throw<InvalidDocumentTransitionException>(
                $"aucune transition ne sort de {sink} (tentative vers {to}).");
            doc.State.Should().Be(sink);
        }
    }

    /// <summary>Invoque la méthode de transition correspondant à l'état cible, avec des arguments TOUJOURS valides
    /// (de sorte que seule la LÉGALITÉ de la transition puisse échouer, jamais une garde d'argument).</summary>
    private static DocumentEvent Invoke(Document doc, DocumentState to) => to switch
    {
        DocumentState.Blocked => doc.MarkBlocked(T0.AddMinutes(5)),
        DocumentState.ReadyToSend => doc.MarkReadyToSend(T0.AddMinutes(5)),
        DocumentState.Sending => doc.BeginSending(T0.AddMinutes(5)),
        DocumentState.Issued => doc.MarkIssued(T0.AddMinutes(5)),
        DocumentState.RejectedByPa => doc.MarkRejectedByPa(T0.AddMinutes(5)),
        DocumentState.TechnicalError => doc.MarkTechnicalError(T0.AddMinutes(5)),
        DocumentState.Superseded => doc.Supersede("F-REMPL", "op", T0.AddMinutes(5)),
        DocumentState.ManuallyHandled => doc.MarkManuallyHandled("motif", "op", T0.AddMinutes(5)),
        DocumentState.Detected => throw new InvalidOperationException("Aucune transition ne vise Detected (état initial)."),
        _ => throw new ArgumentOutOfRangeException(nameof(to)),
    };

    private static Document NewDetected() => Document.CreateDetected(
        Guid.NewGuid(), "SRC-1", "F-2026-001", "FAC", new DateOnly(2026, 5, 14),
        "123456789", "Client SARL", true, 100.00m, 20.00m, 120.00m, "hash-1", T0);

    private static Document InState(DocumentState state) => Document.Reconstitute(
        Guid.NewGuid(), "SRC-1", "F-2026-001", "FAC", new DateOnly(2026, 5, 14),
        "123456789", "Client SARL", true, 100.00m, 20.00m, 120.00m,
        state, "hash-1", paDocumentId: null, mappingVersion: null, firstSeenUtc: T0, lastUpdateUtc: T0);
}
