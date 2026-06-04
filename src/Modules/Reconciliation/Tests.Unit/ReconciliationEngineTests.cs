namespace Liakont.Modules.Reconciliation.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Domain;
using Xunit;

public sealed class ReconciliationEngineTests
{
    private static readonly Guid DocA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DocumentCandidate CandidateA =
        new(DocA, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);

    private static readonly DocumentCandidate CandidateB =
        new(DocB, "FAC-2026-0043", new DateOnly(2026, 1, 16), 200.00m);

    [Fact]
    public void NumberInFileName_AutoLinksHighByFileName()
    {
        var pdf = new PooledPdfContent("p1", "FAC-2026-0042.pdf", ExtractedText: null);

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.AutoLinked);
        decision.MatchedDocumentId.Should().Be(DocA);
        decision.Strategy.Should().Be(MatchStrategy.FileName);
        decision.Confidence.Should().Be(MatchConfidence.High);
    }

    [Fact]
    public void NumberInPdfText_AutoLinksHighByContent()
    {
        var pdf = new PooledPdfContent("p1", "scan-001.pdf", "Facture n° FAC-2026-0042 du 15/01/2026");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.AutoLinked);
        decision.MatchedDocumentId.Should().Be(DocA);
        decision.Strategy.Should().Be(MatchStrategy.PdfContent);
    }

    [Fact]
    public void TwoDistinctNumbersMatch_IsAmbiguous_NotReconciled()
    {
        // Le nom porte un numéro, le texte un AUTRE : ambiguïté ⇒ aucun rapprochement automatique.
        var pdf = new PooledPdfContent("p1", "FAC-2026-0042.pdf", "voir aussi FAC-2026-0043");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.NotReconciled);
        decision.MatchedDocumentId.Should().BeNull();
    }

    [Fact]
    public void DateAndAmount_UniqueCandidate_ProposesManualMedium()
    {
        // Aucun numéro dans le nom/texte, mais date + montant TTC présents et UNIQUES.
        var pdf = new PooledPdfContent("p1", "document.pdf", "Émis le 15/01/2026 — total 1162,80 EUR");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.ProposeManual);
        decision.MatchedDocumentId.Should().Be(DocA);
        decision.Strategy.Should().Be(MatchStrategy.DateAndAmount);
        decision.Confidence.Should().Be(MatchConfidence.Medium);
    }

    [Fact]
    public void DateAndAmount_TwoCandidatesShareDateAndAmount_IsAmbiguous_NotReconciled()
    {
        var twin = new DocumentCandidate(DocB, "FAC-2026-0099", new DateOnly(2026, 1, 15), 1162.80m);
        var pdf = new PooledPdfContent("p1", "document.pdf", "15/01/2026 montant 1162,80");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, twin]);

        decision.Outcome.Should().Be(ReconciliationOutcome.NotReconciled);
    }

    [Fact]
    public void NoSignal_NotReconciled_Orphan()
    {
        var pdf = new PooledPdfContent("p1", "scan-xyz.pdf", "aucune information exploitable");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.NotReconciled);
    }

    [Fact]
    public void NumberPrefixOfLongerToken_DoesNotAutoLink()
    {
        // « FAC-2026-0042 » présent uniquement comme préfixe d'un jeton plus long ⇒ pas de match haute confiance.
        var pdf = new PooledPdfContent("p1", "FAC-2026-00421.pdf", ExtractedText: null);

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA]);

        decision.Outcome.Should().Be(ReconciliationOutcome.NotReconciled);
    }

    [Fact]
    public void AmountWithInvariantDecimalPoint_IsMatched()
    {
        var pdf = new PooledPdfContent("p1", "document.pdf", "date 2026-01-15 total 1162.80");

        ReconciliationDecision decision = ReconciliationEngine.Decide(pdf, [CandidateA, CandidateB]);

        decision.Outcome.Should().Be(ReconciliationOutcome.ProposeManual);
        decision.MatchedDocumentId.Should().Be(DocA);
    }
}
