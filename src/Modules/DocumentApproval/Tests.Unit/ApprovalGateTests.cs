namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Règle de gate (ADR-0028 §5, INV-APPROVAL-4) — fonction pure <see cref="ApprovalGate"/>. État nécessaire +
/// niveau de preuve ≥ exigence tenant (par slot) + forme expresse self-billing (hors tacite).
/// </summary>
public sealed class ApprovalGateTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();

    [Fact]
    public void Pending_Does_Not_Open_The_Gate()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        ApprovalGate.Evaluate(v, SignatureLevel.Recorded).IsOpen.Should().BeFalse("seul Validated/TacitlyValidated ouvre");
    }

    [Fact]
    public void Recorded_Acceptance_Satisfies_A_Recorded_Requirement()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);
        v.Validate(SignatureLevel.Recorded);

        ApprovalGate.Evaluate(v, SignatureLevel.Recorded).IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Bare_Recorded_Does_Not_Satisfy_An_AES_Requirement()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);
        v.Validate(SignatureLevel.Recorded);

        var decision = ApprovalGate.Evaluate(v, SignatureLevel.AES);
        decision.IsOpen.Should().BeFalse("un Recorded nu ne franchit pas une exigence AES (ADR-0028 §5 cond. 2)");
        decision.Reason.Should().Contain("Recorded");
    }

    [Fact]
    public void A_Higher_Proof_Satisfies_A_Lower_Requirement()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.MandateSignature, deadlineUtc: null);
        v.Validate(SignatureLevel.QES);

        ApprovalGate.Evaluate(v, SignatureLevel.SES).IsOpen.Should().BeTrue("QES ≥ SES sur l'échelle d'assurance");
    }

    [Fact]
    public void Tacit_Validation_Only_Satisfies_A_Recorded_Requirement()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);
        v.MarkTacitlyValidated();

        ApprovalGate.Evaluate(v, SignatureLevel.Recorded).IsOpen.Should().BeTrue("une tacite satisfait Recorded");
        ApprovalGate.Evaluate(v, SignatureLevel.AES).IsOpen.Should().BeFalse("une tacite (sans preuve) ne satisfait que Recorded");
    }

    [Fact]
    public void Express_Form_Condition_Does_Not_Apply_To_Tacit_Validation()
    {
        // INV-ACCEPT-3 / ADR-0028 §5 : la condition 3 (forme expresse) NE s'applique PAS à TacitlyValidated —
        // sinon on bloquerait à tort une tacite valide. Le self-billing exige une forme expresse, et pourtant
        // une tacite ouvre le gate au niveau Recorded.
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);
        v.MarkTacitlyValidated();

        ApprovalGate.Evaluate(v, SignatureLevel.Recorded).IsOpen.Should().BeTrue();
    }

    [Fact]
    public void SelfBilling_Validated_Without_Recorded_Express_Acceptance_Is_Closed()
    {
        // Condition 3 : une transition Validated EXPRESSE d'un self-billing exige une acceptation enregistrée
        // explicite. On reconstitue un Validated SANS forme expresse → gate fermé.
        var v = DocumentValidation.Reconstitute(
            Company, Document, ValidationPurpose.SelfBilledAcceptance, attempt: 1,
            ValidationState.Validated, SignatureLevel.Recorded, expressAcceptanceRecorded: false,
            deadlineUtc: null, createdAt: DateTimeOffset.UtcNow, updatedAt: DateTimeOffset.UtcNow);

        ApprovalGate.Evaluate(v, SignatureLevel.Recorded).IsOpen.Should().BeFalse(
            "self-billing Validated expresse sans acceptation enregistrée → fermé (ADR-0028 §5 cond. 3)");
    }

    [Fact]
    public void MultiParty_Gate_Requires_Every_Slot_To_Meet_The_Required_Level()
    {
        var v = DocumentValidation.Create(
            Company, Document, ValidationPurpose.MultiPartySignature, deadlineUtc: null, signerIds: ["alice", "bob"]);
        v.ApproveSlot("alice", SignatureLevel.AES);
        v.ApproveSlot("bob", SignatureLevel.SES);
        v.State.Should().Be(ValidationState.Validated);

        ApprovalGate.Evaluate(v, SignatureLevel.AES).IsOpen.Should().BeFalse(
            "un slot sous-niveau (bob = SES) n'ouvre pas un gate exigeant AES (évaluation PAR slot, §8)");
        ApprovalGate.Evaluate(v, SignatureLevel.SES).IsOpen.Should().BeTrue("tous les slots ≥ SES");
    }
}
