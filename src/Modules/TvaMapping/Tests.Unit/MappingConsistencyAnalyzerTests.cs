namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Xunit;

/// <summary>
/// Contrôle de cohérence du paramétrage TVA (lot FIX03), symétrique du rapport de couverture (TVA03).
/// Vérifie que les règles MORTES sont signalées (part non consultée, code jamais observé) sans jamais
/// bloquer — c'est un signal (CLAUDE.md n°3) — et que la dérivation des parts consultées suit
/// l'activation du vertical enchères (D4), sans inventer de règle fiscale.
/// </summary>
public sealed class MappingConsistencyAnalyzerTests
{
    private static readonly MappingPart[] AutreOnly = { MappingPart.Autre };
    private static readonly MappingPart[] AllParts = { MappingPart.Adjudication, MappingPart.Frais, MappingPart.Autre };
    private static readonly string[] ObservedA = { "R-A" };
    private static readonly string[] ObservedAB = { "R-A", "R-B" };

    [Fact]
    public void AuctionVerticalOff_OnlyAutre_IsConsulted()
    {
        var parts = ConsultedMappingParts.For(auctionVerticalEnabled: false);

        parts.Should().BeEquivalentTo(AutreOnly);
    }

    [Fact]
    public void AuctionVerticalOn_AllParts_AreConsulted()
    {
        var parts = ConsultedMappingParts.For(auctionVerticalEnabled: true);

        parts.Should().BeEquivalentTo(AllParts);
    }

    [Fact]
    public void VerticalOff_AdjudicationRule_IsDead_PartNotConsulted()
    {
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Adjudication), Rule("R-A", MappingPart.Autre) },
            auctionVerticalEnabled: false,
            observed: ObservedA);

        report.DeadRules.Should().ContainSingle();
        var dead = report.DeadRules[0];
        dead.SourceRegimeCode.Should().Be("R-A");
        dead.Part.Should().Be(MappingPart.Adjudication);
        dead.Reasons.Should().Equal(DeadRuleReason.PartNotConsulted);
    }

    [Fact]
    public void VerticalOn_AdjudicationRule_IsNotDead_WhenObserved()
    {
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Adjudication), Rule("R-A", MappingPart.Frais) },
            auctionVerticalEnabled: true,
            observed: ObservedA);

        report.HasDeadRules.Should().BeFalse("le tenant a déclaré le vertical enchères : ces parts sont en scope");
    }

    [Fact]
    public void RegimeNeverObserved_IsFlagged_WhenObservationsExist()
    {
        var report = Analyze(
            rules: new[] { Rule("TYPO-X", MappingPart.Autre) },
            auctionVerticalEnabled: false,
            observed: ObservedAB);

        report.DeadRules.Should().ContainSingle();
        report.DeadRules[0].Reasons.Should().Equal(DeadRuleReason.RegimeNeverObserved);
    }

    [Fact]
    public void RegimeNeverObserved_IsNotFlagged_OnFreshTenant_NoObservations()
    {
        // Aucun document ingéré encore : l'absence d'observation ne prouve rien — on ne signale pas
        // (sinon TOUTES les règles seraient « mortes » sur un tenant vierge).
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Autre), Rule("R-B", MappingPart.Autre) },
            auctionVerticalEnabled: false,
            observed: Array.Empty<string>());

        report.HasDeadRules.Should().BeFalse();
    }

    [Fact]
    public void Rule_CanCarry_BothReasons()
    {
        var report = Analyze(
            rules: new[] { Rule("TYPO-X", MappingPart.Adjudication) },
            auctionVerticalEnabled: false,
            observed: ObservedA);

        report.DeadRules[0].Reasons.Should()
            .Equal(DeadRuleReason.PartNotConsulted, DeadRuleReason.RegimeNeverObserved);
    }

    [Fact]
    public void CodeMatching_IsOrdinalCaseSensitive()
    {
        // Cohérent avec TvaMapper (INV-011) : un code de casse différente ne matcherait pas à l'exécution.
        var report = Analyze(
            rules: new[] { Rule("r-a", MappingPart.Autre) },
            auctionVerticalEnabled: false,
            observed: ObservedA);

        report.DeadRules[0].Reasons.Should().Equal(DeadRuleReason.RegimeNeverObserved);
    }

    [Fact]
    public void NoDeadRules_WhenAllConsultedAndObserved()
    {
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Autre) },
            auctionVerticalEnabled: false,
            observed: ObservedA);

        report.HasDeadRules.Should().BeFalse();
        report.IsTableConfigured.Should().BeTrue();
    }

    private static MappingConsistencyReport Analyze(
        IReadOnlyList<MappingRuleConsistencyView> rules,
        bool auctionVerticalEnabled,
        IReadOnlyCollection<string> observed)
    {
        return MappingConsistencyAnalyzer.Analyze(
            rules,
            ConsultedMappingParts.For(auctionVerticalEnabled),
            observed,
            tableConfigured: true);
    }

    private static MappingRuleConsistencyView Rule(string code, MappingPart part) => new()
    {
        SourceRegimeCode = code,
        Part = part,
    };
}
