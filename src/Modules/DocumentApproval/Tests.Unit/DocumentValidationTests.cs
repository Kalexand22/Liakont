namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Agrégat <see cref="DocumentValidation"/> (ADR-0028) : transitions mono-partie gardées par le sous-graphe de
/// purpose, exclusion du ré-essai pour le self-billing, aucun retour arrière depuis un terminal.
/// </summary>
public sealed class DocumentValidationTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();

    [Fact]
    public void SelfBilling_Validate_Reaches_Validated_With_Recorded_Proof()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        v.Validate(SignatureLevel.Recorded);

        v.State.Should().Be(ValidationState.Validated);
        v.ProofLevel.Should().Be(SignatureLevel.Recorded);
        v.ExpressAcceptanceRecorded.Should().BeTrue();
        v.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void SelfBilling_Cannot_Go_To_A_State_Outside_Its_Subgraph()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        v.Invoking(x => x.MarkInProgress()).Should().Throw<InvalidOperationException>(
            "ValidationInProgress est hors du sous-graphe self-billing (ADR-0028 §4)");
        v.Invoking(x => x.Reject()).Should().Throw<InvalidOperationException>(
            "Rejected est hors du sous-graphe self-billing (il utilise Contested)");
        v.Invoking(x => x.Expire()).Should().Throw<InvalidOperationException>(
            "Expired est hors du sous-graphe self-billing");
        v.State.Should().Be(ValidationState.PendingValidation, "aucune transition refusée ne mute l'état");
    }

    [Fact]
    public void No_Transition_From_A_Terminal_State()
    {
        var contested = DocumentValidation.Reconstitute(
            Company, Document, ValidationPurpose.SelfBilledAcceptance, attempt: 1,
            ValidationState.Contested, SignatureLevel.None, expressAcceptanceRecorded: false,
            deadlineUtc: null, createdAt: DateTimeOffset.UtcNow, updatedAt: DateTimeOffset.UtcNow);

        contested.Invoking(x => x.Validate(SignatureLevel.Recorded)).Should().Throw<InvalidOperationException>();
        contested.Invoking(x => x.MarkTacitlyValidated()).Should().Throw<InvalidOperationException>();
        contested.State.Should().Be(ValidationState.Contested);
    }

    [Fact]
    public void SelfBilling_Is_Excluded_From_Retry()
    {
        var act = () => DocumentValidation.Create(
            Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null, attempt: 2);

        act.Should().Throw<InvalidOperationException>("le self-billing est exclu du ré-essai — Contested définitif (ADR-0028 §6)");
    }

    [Fact]
    public void SignaturePurpose_Allows_InProgress_Then_Validated_With_Signature_Level()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.MandateSignature, deadlineUtc: null);

        v.MarkInProgress();
        v.State.Should().Be(ValidationState.ValidationInProgress);

        v.Validate(SignatureLevel.AES);
        v.State.Should().Be(ValidationState.Validated);
        v.ProofLevel.Should().Be(SignatureLevel.AES);
    }

    [Fact]
    public void SignaturePurpose_Supports_A_Second_Attempt_After_Terminal_Failure()
    {
        var v = DocumentValidation.Create(
            Company, Document, ValidationPurpose.MandateSignature, deadlineUtc: null, attempt: 2);

        v.Attempt.Should().Be(2);
        v.State.Should().Be(ValidationState.PendingValidation);
    }

    [Fact]
    public void Express_Validation_Requires_A_Non_None_Proof_Level()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.MandateSignature, deadlineUtc: null);

        v.Invoking(x => x.Validate(SignatureLevel.None)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tacit_Validation_Sets_Recorded_Proof_And_No_Express_Form()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.SelfBilledAcceptance, deadlineUtc: null);

        v.MarkTacitlyValidated();

        v.State.Should().Be(ValidationState.TacitlyValidated);
        v.ProofLevel.Should().Be(SignatureLevel.Recorded);
        v.ExpressAcceptanceRecorded.Should().BeFalse("une bascule tacite n'est pas une acceptation expresse");
    }
}
