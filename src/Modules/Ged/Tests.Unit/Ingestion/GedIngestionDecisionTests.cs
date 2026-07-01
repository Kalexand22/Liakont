namespace Liakont.Modules.Ged.Tests.Unit.Ingestion;

using FluentAssertions;
using Liakont.Modules.Ged.Domain.Ingestion;
using Xunit;

/// <summary>
/// Golden de la décision d'anti-doublon GED (GED05b, F19 §4.3, INV-GED-06). Prouve les TROIS cas fermés et
/// l'ORDRE d'évaluation (doublon strict AVANT altération : un renvoi du même contenu n'est jamais une fausse
/// altération). Logique RE-COPIÉE du canal fiscal (RL-01) — testée indépendamment.
/// </summary>
public sealed class GedIngestionDecisionTests
{
    private const string HashA = "aaaa";
    private const string HashB = "bbbb";

    [Fact]
    public void Unknown_payload_without_prior_source_reference_is_AcceptedNew()
    {
        var decision = GedIngestionDecision.Evaluate(payloadAlreadyKnown: false, existingHashForSourceReference: null, newPayloadHash: HashA);

        decision.Kind.Should().Be(GedIngestionDecisionKind.AcceptedNew);
        decision.IsAccepted.Should().BeTrue();
        decision.IsAlteration.Should().BeFalse();
        decision.PreviousPayloadHash.Should().BeNull();
    }

    [Fact]
    public void Known_source_reference_with_a_different_hash_is_AcceptedAltered()
    {
        var decision = GedIngestionDecision.Evaluate(payloadAlreadyKnown: false, existingHashForSourceReference: HashA, newPayloadHash: HashB);

        decision.Kind.Should().Be(GedIngestionDecisionKind.AcceptedAltered);
        decision.IsAccepted.Should().BeTrue();
        decision.IsAlteration.Should().BeTrue();
        decision.PreviousPayloadHash.Should().Be(HashA);
    }

    [Fact]
    public void Same_source_reference_and_same_hash_is_AcceptedNew_not_an_alteration()
    {
        // Référence connue mais empreinte IDENTIQUE (pas encore enregistrée comme doublon) : pas une altération.
        var decision = GedIngestionDecision.Evaluate(payloadAlreadyKnown: false, existingHashForSourceReference: HashA, newPayloadHash: HashA);

        decision.Kind.Should().Be(GedIngestionDecisionKind.AcceptedNew);
        decision.IsAlteration.Should().BeFalse();
    }

    [Fact]
    public void Known_payload_is_Duplicate_even_when_a_prior_hash_differs()
    {
        // ORDRE : le doublon strict est testé AVANT l'altération — un renvoi du même contenu est un doublon.
        var decision = GedIngestionDecision.Evaluate(payloadAlreadyKnown: true, existingHashForSourceReference: HashA, newPayloadHash: HashB);

        decision.Kind.Should().Be(GedIngestionDecisionKind.Duplicate);
        decision.IsAccepted.Should().BeFalse();
        decision.IsAlteration.Should().BeFalse();
        decision.PreviousPayloadHash.Should().BeNull();
    }
}
