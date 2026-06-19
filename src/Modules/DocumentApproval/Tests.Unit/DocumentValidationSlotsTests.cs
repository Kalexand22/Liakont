namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Agrégation N-parties par SLOTS identifiés idempotents (ADR-0028 §8, INV-APPROVAL-7) : complétude = tous les
/// slots distincts remplis (jamais un compteur) ; niveau de preuve PAR slot ; un slot refusé bascule l'agrégat
/// en terminal négatif immédiatement.
/// </summary>
public sealed class DocumentValidationSlotsTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();

    [Fact]
    public void MultiParty_Requires_At_Least_One_Signer()
    {
        var act = () => DocumentValidation.Create(
            Company, Document, ValidationPurpose.MultiPartySignature, deadlineUtc: null);

        act.Should().Throw<ArgumentException>("un document co-signé exige au moins un slot");
    }

    [Fact]
    public void MultiParty_Rejects_Duplicate_Signers()
    {
        var act = () => DocumentValidation.Create(
            Company, Document, ValidationPurpose.MultiPartySignature, deadlineUtc: null,
            signerIds: ["alice", "alice"]);

        act.Should().Throw<ArgumentException>("les slots doivent être distincts");
    }

    [Fact]
    public void Completeness_Requires_All_Distinct_Slots_Approved_Not_A_Count()
    {
        var v = NewMultiParty("alice", "bob");

        v.ApproveSlot("alice", SignatureLevel.AES);
        v.State.Should().Be(ValidationState.PendingValidation, "un seul des deux slots est rempli");

        // Idempotence par SignerId : ré-approuver « alice » ne complète RIEN (jamais un compteur).
        v.ApproveSlot("alice", SignatureLevel.AES);
        v.State.Should().Be(ValidationState.PendingValidation, "l'idempotence empêche un faux total d'événements");

        v.ApproveSlot("bob", SignatureLevel.AES);
        v.State.Should().Be(ValidationState.Validated, "tous les slots distincts sont approuvés");
        v.Slots.Should().OnlyContain(s => s.IsApproved);
    }

    [Fact]
    public void Rejecting_A_Slot_Immediately_Terminates_The_Aggregate_Negatively()
    {
        var v = NewMultiParty("alice", "bob");

        v.ApproveSlot("alice", SignatureLevel.AES);
        v.RejectSlot("bob");

        v.State.Should().Be(ValidationState.Rejected, "terminaison négative immédiate (ADR-0028 §8)");
        v.IsTerminal.Should().BeTrue();
        v.Invoking(x => x.ApproveSlot("bob", SignatureLevel.AES))
            .Should().Throw<InvalidOperationException>("aucune mutation de slot sur un agrégat terminal");
    }

    [Fact]
    public void Approving_An_Unknown_Signer_Throws()
    {
        var v = NewMultiParty("alice");

        v.Invoking(x => x.ApproveSlot("mallory", SignatureLevel.AES))
            .Should().Throw<InvalidOperationException>("un signataire hors de l'ensemble fixe est refusé");
    }

    [Fact]
    public void Slot_Levels_Are_Tracked_Per_Slot()
    {
        var v = NewMultiParty("alice", "bob");

        v.ApproveSlot("alice", SignatureLevel.QES);
        v.ApproveSlot("bob", SignatureLevel.SES);

        v.Slots.Single(s => s.SignerId == "alice").ProofLevel.Should().Be(SignatureLevel.QES);
        v.Slots.Single(s => s.SignerId == "bob").ProofLevel.Should().Be(SignatureLevel.SES);
    }

    [Fact]
    public void SingleParty_Transitions_Are_Forbidden_On_A_Slot_Purpose()
    {
        var v = NewMultiParty("alice");

        v.Invoking(x => x.Validate(SignatureLevel.AES))
            .Should().Throw<InvalidOperationException>("un purpose N-parties se valide par ses slots, pas en mono-partie");
    }

    [Fact]
    public void Slot_Operations_Are_Forbidden_On_A_Single_Party_Purpose()
    {
        var v = DocumentValidation.Create(Company, Document, ValidationPurpose.MandateSignature, deadlineUtc: null);

        v.Invoking(x => x.ApproveSlot("alice", SignatureLevel.AES))
            .Should().Throw<InvalidOperationException>("un purpose mono-partie n'a pas de slots");
    }

    private static DocumentValidation NewMultiParty(params string[] signerIds)
        => DocumentValidation.Create(
            Company, Document, ValidationPurpose.MultiPartySignature, deadlineUtc: null, signerIds: signerIds);
}
