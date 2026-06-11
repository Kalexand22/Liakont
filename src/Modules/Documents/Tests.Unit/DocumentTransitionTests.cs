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

    // Preuves d'émission / de rejet (F06 §3 / TRK04) — JSON fictifs, valides pour exercer la capture des snapshots.
    private static readonly IssuanceSnapshots Issuance = new(
        payloadSnapshot: "{\"documentNumber\":\"F-2026-001\",\"totalGross\":120.00}",
        paResponseSnapshot: "{\"paDocumentId\":\"PA-123\",\"taxReportId\":\"TR-9\"}",
        mappingTrace: "{\"rule\":\"S->20\",\"version\":\"2026.1\"}");

    private static readonly RejectionSnapshots Rejection = new(
        payloadSnapshot: "{\"documentNumber\":\"F-2026-001\",\"totalGross\":120.00}",
        paResponseSnapshot: "{\"error\":\"INVALID_FORMAT\",\"message\":\"Format rejeté\"}");

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

        var issued = doc.MarkIssued(Issuance, T0.AddMinutes(3));
        doc.State.Should().Be(DocumentState.Issued);
        doc.LastUpdateUtc.Should().Be(T0.AddMinutes(3));
        issued.EventType.Should().Be(DocumentEventType.DocumentIssued);
    }

    [Fact]
    public void Issued_Event_Carries_The_Three_Proof_Snapshots()
    {
        var doc = InState(DocumentState.Sending);

        var issued = doc.MarkIssued(Issuance, T0.AddMinutes(1));

        // Les trois snapshots (F06 §3 / TRK04) constituent la preuve complète d'un document émis.
        issued.PayloadSnapshot.Should().Be(Issuance.PayloadSnapshot);
        issued.PaResponseSnapshot.Should().Be(Issuance.PaResponseSnapshot);
        issued.MappingTrace.Should().Be(Issuance.MappingTrace);
        issued.OperatorIdentity.Should().BeNull("la décision de la PA n'est pas une action opérateur.");
    }

    [Fact]
    public void Rejected_Event_Carries_Payload_And_Pa_Response_But_No_Mapping_Trace()
    {
        var doc = InState(DocumentState.Sending);

        var rejected = doc.MarkRejectedByPa(Rejection, T0.AddMinutes(1), reason: "Format rejeté");

        doc.State.Should().Be(DocumentState.RejectedByPa);
        rejected.PayloadSnapshot.Should().Be(Rejection.PayloadSnapshot);
        rejected.PaResponseSnapshot.Should().Be(Rejection.PaResponseSnapshot);
        rejected.MappingTrace.Should().BeNull("un document rejeté n'a pas été émis : la trace de mapping n'est pas requise (F06 §3).");
        rejected.Detail.Should().Contain("Format rejeté");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Issuance_Snapshots_Require_A_Non_Blank_Payload(string blank)
    {
        var act = () => new IssuanceSnapshots(blank, "{\"pa\":1}", "{\"map\":1}");

        act.Should().Throw<ArgumentException>().WithParameterName("payloadSnapshot");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Issuance_Snapshots_Require_A_Non_Blank_Mapping_Trace(string blank)
    {
        var act = () => new IssuanceSnapshots("{\"payload\":1}", "{\"pa\":1}", blank);

        act.Should().Throw<ArgumentException>().WithParameterName("mappingTrace");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejection_Snapshots_Require_A_Non_Blank_Pa_Response(string blank)
    {
        var act = () => new RejectionSnapshots("{\"payload\":1}", blank);

        act.Should().Throw<ArgumentException>().WithParameterName("paResponseSnapshot");
    }

    [Fact]
    public void MarkIssued_Rejects_Null_Snapshots()
    {
        var doc = InState(DocumentState.Sending);

        var act = () => doc.MarkIssued(null!, T0.AddMinutes(1));

        act.Should().Throw<ArgumentNullException>();
        doc.State.Should().Be(DocumentState.Sending, "snapshots manquants : l'état n'a pas changé.");
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
            occurredAtUtc: T0.AddMinutes(1),
            operatorName: "Alice Comptable");

        doc.State.Should().Be(DocumentState.ManuallyHandled);
        evt.EventType.Should().Be(DocumentEventType.DocumentManuallyHandled);
        evt.OperatorIdentity.Should().Be("alice@cmp");
        evt.OperatorName.Should().Be("Alice Comptable", "le nom d'affichage est porté par l'événement (FIX305).");
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
            occurredAtUtc: T0.AddMinutes(1),
            operatorName: "Carole Comptable");

        doc.State.Should().Be(DocumentState.Superseded);
        evt.EventType.Should().Be(DocumentEventType.DocumentSuperseded);
        evt.OperatorIdentity.Should().Be("carol@cmp");
        evt.OperatorName.Should().Be("Carole Comptable", "le nom d'affichage est porté par l'événement (FIX305).");
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

    [Fact]
    public void ConfirmBuyerAsIndividual_From_Blocked_Sets_Flag_And_Records_Operator_Without_State_Change()
    {
        // Verdict garde-fou B2B/B2C (API02b, F08 §A.4) : pose le marqueur persistant + un fait d'audit opérateur,
        // SANS changer l'état (le document reste Blocked ; la re-vérification le débloque ensuite).
        var doc = InState(DocumentState.Blocked);

        var evt = doc.ConfirmBuyerAsIndividual(operatorIdentity: "alice@cmp", occurredAtUtc: T0.AddMinutes(1), operatorName: "Alice Comptable");

        doc.State.Should().Be(DocumentState.Blocked, "le verdict B2C ne change pas l'état (recheck requis pour débloquer).");
        doc.BuyerConfirmedAsIndividual.Should().BeTrue();
        doc.LastUpdateUtc.Should().Be(T0.AddMinutes(1));
        evt.EventType.Should().Be(DocumentEventType.DocumentBuyerConfirmedB2C);
        evt.OperatorIdentity.Should().Be("alice@cmp");
        evt.OperatorName.Should().Be("Alice Comptable", "le nom d'affichage est porté par l'événement (FIX305).");
        evt.DocumentId.Should().Be(doc.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfirmBuyerAsIndividual_Requires_An_Operator_Identity(string blank)
    {
        var doc = InState(DocumentState.Blocked);

        var act = () => doc.ConfirmBuyerAsIndividual(blank, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("operatorIdentity");
        doc.BuyerConfirmedAsIndividual.Should().BeFalse("identité manquante : le marqueur n'est pas posé.");
    }

    [Theory]
    [InlineData(DocumentState.Detected)]
    [InlineData(DocumentState.ReadyToSend)]
    [InlineData(DocumentState.Issued)]
    [InlineData(DocumentState.ManuallyHandled)]
    public void ConfirmBuyerAsIndividual_Is_Rejected_Outside_Blocked(DocumentState state)
    {
        // Le verdict du garde-fou ne s'applique qu'à un document bloqué.
        var doc = InState(state);

        var act = () => doc.ConfirmBuyerAsIndividual("alice@cmp", T0);

        act.Should().Throw<InvalidOperationException>();
        doc.BuyerConfirmedAsIndividual.Should().BeFalse("hors Blocked : le marqueur n'est pas posé.");
        doc.State.Should().Be(state, "verdict refusé : l'état n'a pas changé.");
    }

    [Fact]
    public void RecordRecheckStillBlocked_From_Blocked_Records_Operator_And_Reason_Without_State_Change()
    {
        // FIX02 : une re-vérification restée bloquée trace le geste opérateur + le motif RÉÉVALUÉ comme fait
        // d'audit append-only, SANS changer l'état (Blocked → Blocked interdit). Le motif porté devient le motif
        // courant affiché.
        var doc = InState(DocumentState.Blocked);

        var evt = doc.RecordRecheckStillBlocked("Acheteur professionnel non confirmé.", operatorIdentity: "alice@cmp", occurredAtUtc: T0.AddMinutes(2), operatorName: "Alice Comptable");

        doc.State.Should().Be(DocumentState.Blocked, "le recheck toujours bloqué ne change pas l'état.");
        doc.LastUpdateUtc.Should().Be(T0.AddMinutes(2));
        evt.EventType.Should().Be(DocumentEventType.DocumentRecheckedStillBlocked);
        evt.OperatorIdentity.Should().Be("alice@cmp");
        evt.OperatorName.Should().Be("Alice Comptable", "le nom d'affichage est porté par l'événement (FIX305).");
        evt.Detail.Should().Be("Acheteur professionnel non confirmé.", "le motif réévalué porté par l'événement = le motif courant.");
        evt.DocumentId.Should().Be(doc.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordRecheckStillBlocked_Requires_An_Operator_Identity(string blank)
    {
        var doc = InState(DocumentState.Blocked);

        var act = () => doc.RecordRecheckStillBlocked("Motif réévalué.", blank, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("operatorIdentity");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordRecheckStillBlocked_Requires_A_Reevaluated_Reason(string blank)
    {
        var doc = InState(DocumentState.Blocked);

        var act = () => doc.RecordRecheckStillBlocked(blank, "alice@cmp", T0);

        act.Should().Throw<ArgumentException>().WithParameterName("reevaluatedReason");
    }

    [Theory]
    [InlineData(DocumentState.Detected)]
    [InlineData(DocumentState.ReadyToSend)]
    [InlineData(DocumentState.Issued)]
    public void RecordRecheckStillBlocked_Is_Rejected_Outside_Blocked(DocumentState state)
    {
        // Un recheck-toujours-bloqué ne se trace que sur un document bloqué (cohérent avec la pré-vérification).
        var doc = InState(state);

        var act = () => doc.RecordRecheckStillBlocked("Motif réévalué.", "alice@cmp", T0);

        act.Should().Throw<InvalidOperationException>();
        doc.State.Should().Be(state, "recheck refusé : l'état n'a pas changé.");
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
        DocumentState.Issued => doc.MarkIssued(Issuance, T0.AddMinutes(5)),
        DocumentState.RejectedByPa => doc.MarkRejectedByPa(Rejection, T0.AddMinutes(5)),
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
