namespace Liakont.Modules.Ingestion.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Ingestion.Domain;
using Xunit;

/// <summary>
/// Décision anti-doublon pure (PIV04) : doublon / accepté / accepté+altéré — INV-INGESTION-009,
/// INV-INGESTION-010.
/// </summary>
public sealed class DocumentIngestionDecisionTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Known_Payload_Hash_Is_A_Duplicate()
    {
        var decision = DocumentIngestionDecision.Evaluate(
            payloadAlreadyKnown: true,
            existingHashForSourceReference: HashA,
            newPayloadHash: HashA);

        decision.Kind.Should().Be(IngestionDecisionKind.Duplicate);
        decision.IsAccepted.Should().BeFalse();
        decision.IsAlteration.Should().BeFalse();
        decision.PreviousPayloadHash.Should().BeNull();
    }

    [Fact]
    public void Re_Push_Of_Same_Payload_Is_A_Duplicate_Not_An_Alteration()
    {
        // Re-push complet après réinstallation d'agent : même réf, même empreinte → doublon prioritaire.
        var decision = DocumentIngestionDecision.Evaluate(
            payloadAlreadyKnown: true,
            existingHashForSourceReference: HashA,
            newPayloadHash: HashA);

        decision.Kind.Should().Be(IngestionDecisionKind.Duplicate);
    }

    [Fact]
    public void Unknown_Source_Reference_Is_Accepted_As_New()
    {
        var decision = DocumentIngestionDecision.Evaluate(
            payloadAlreadyKnown: false,
            existingHashForSourceReference: null,
            newPayloadHash: HashA);

        decision.Kind.Should().Be(IngestionDecisionKind.AcceptedNew);
        decision.IsAccepted.Should().BeTrue();
        decision.IsAlteration.Should().BeFalse();
    }

    [Fact]
    public void Known_Source_Reference_With_Different_Hash_Is_Accepted_And_Altered()
    {
        var decision = DocumentIngestionDecision.Evaluate(
            payloadAlreadyKnown: false,
            existingHashForSourceReference: HashA,
            newPayloadHash: HashB);

        decision.Kind.Should().Be(IngestionDecisionKind.AcceptedAltered);
        decision.IsAccepted.Should().BeTrue();
        decision.IsAlteration.Should().BeTrue();
        decision.PreviousPayloadHash.Should().Be(HashA);
    }

    [Fact]
    public void Empty_Hash_Is_Rejected()
    {
        var act = () => DocumentIngestionDecision.Evaluate(false, null, string.Empty);

        act.Should().Throw<System.ArgumentException>();
    }
}
