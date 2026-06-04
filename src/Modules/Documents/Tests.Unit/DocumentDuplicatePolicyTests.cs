namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Deduplication;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// Les quatre règles d'anti-doublon de F06 §4 (item TRK03), testées UNE PAR UNE sur la fonction pure
/// <see cref="DocumentDuplicatePolicy"/> — aucune règle inventée, transcription verbatim de la spec
/// (CLAUDE.md n°2). Couvre aussi la précédence des règles et le garde-fou d'empreinte.
/// </summary>
public sealed class DocumentDuplicatePolicyTests
{
    private static readonly Guid IssuedId = Guid.NewGuid();
    private static readonly Guid RejectedId = Guid.NewGuid();
    private static readonly Guid HashTwinId = Guid.NewGuid();

    [Fact]
    public void Rule_42_Prior_Issued_By_Functional_Key_Blocks_As_Duplicate()
    {
        var priors = new[] { new PriorDocumentMatch(IssuedId, DocumentState.Issued) };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: null);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.BlockedAlreadyIssued);
        decision.RelatedDocumentId.Should().Be(IssuedId);
        decision.MaySend.Should().BeFalse("un document déjà émis ne se renvoie pas (F06 §4.2).");
    }

    [Fact]
    public void Rule_43_Prior_RejectedByPa_Allows_Resend_And_Exposes_Document_To_Supersede()
    {
        var priors = new[] { new PriorDocumentMatch(RejectedId, DocumentState.RejectedByPa) };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: null);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.ResendSupersedingRejected);
        decision.RelatedDocumentId.Should().Be(RejectedId, "l'ancien rejeté doit passer Superseded (F06 §4.3).");
        decision.MaySend.Should().BeTrue("le renvoi après rejet est autorisé (F06 §4.3).");
    }

    [Fact]
    public void Rule_44_Same_Payload_Hash_Already_Issued_Blocks_As_Strict_Duplicate()
    {
        var decision = DocumentDuplicatePolicy.Decide(
            Array.Empty<PriorDocumentMatch>(),
            issuedDocumentIdWithSamePayloadHash: HashTwinId);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.BlockedStrictDuplicate);
        decision.RelatedDocumentId.Should().Be(HashTwinId);
        decision.MaySend.Should().BeFalse("une ré-extraction d'un contenu déjà émis est un doublon strict (F06 §4.4).");
    }

    [Fact]
    public void Rule_45_No_Blocking_Prior_Authorizes_Send()
    {
        var decision = DocumentDuplicatePolicy.Decide(
            Array.Empty<PriorDocumentMatch>(),
            issuedDocumentIdWithSamePayloadHash: null);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.Send);
        decision.RelatedDocumentId.Should().BeNull();
        decision.MaySend.Should().BeTrue();
    }

    [Fact]
    public void Issued_Takes_Precedence_Over_Rejected_On_The_Same_Functional_Key()
    {
        // F06 §4 énumère les règles dans l'ordre 2 (Issued) puis 3 (Rejected) : si les deux états coexistent
        // pour la même clé, le doublon émis l'emporte (bloque) — on ne renvoie jamais un numéro déjà émis.
        var priors = new[]
        {
            new PriorDocumentMatch(RejectedId, DocumentState.RejectedByPa),
            new PriorDocumentMatch(IssuedId, DocumentState.Issued),
        };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: null);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.BlockedAlreadyIssued);
        decision.RelatedDocumentId.Should().Be(IssuedId);
    }

    [Fact]
    public void Functional_Key_Match_Takes_Precedence_Over_The_Hash_Guard()
    {
        // Antécédent Rejected (4.3, renvoi autorisé) ET une empreinte jumelle déjà émise (4.4) : F06 liste
        // 4.3 avant 4.4 — la clé fonctionnelle tranche d'abord.
        var priors = new[] { new PriorDocumentMatch(RejectedId, DocumentState.RejectedByPa) };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: HashTwinId);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.ResendSupersedingRejected);
        decision.RelatedDocumentId.Should().Be(RejectedId);
    }

    [Theory]
    [InlineData(DocumentState.Detected)]
    [InlineData(DocumentState.Blocked)]
    [InlineData(DocumentState.ReadyToSend)]
    [InlineData(DocumentState.Sending)]
    [InlineData(DocumentState.TechnicalError)]
    [InlineData(DocumentState.Superseded)]
    [InlineData(DocumentState.ManuallyHandled)]
    public void Non_Issued_Non_Rejected_Priors_Do_Not_Block_Without_A_Hash_Twin(DocumentState priorState)
    {
        var priors = new[] { new PriorDocumentMatch(Guid.NewGuid(), priorState) };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: null);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.Send,
            "seuls Issued et RejectedByPa de même clé fonctionnelle changent le verdict (F06 §4.2/4.3).");
    }

    [Fact]
    public void In_Flight_Prior_Plus_Hash_Twin_Falls_Through_To_The_Strict_Duplicate_Guard()
    {
        var priors = new[] { new PriorDocumentMatch(Guid.NewGuid(), DocumentState.Sending) };

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedDocumentIdWithSamePayloadHash: HashTwinId);

        decision.Outcome.Should().Be(DocumentDuplicateOutcome.BlockedStrictDuplicate);
        decision.RelatedDocumentId.Should().Be(HashTwinId);
    }

    [Fact]
    public void Decide_Rejects_A_Null_Prior_Collection()
    {
        var act = () => DocumentDuplicatePolicy.Decide(null!, issuedDocumentIdWithSamePayloadHash: null);

        act.Should().Throw<ArgumentNullException>();
    }
}
