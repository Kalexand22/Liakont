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
/// bloquer — c'est un signal (CLAUDE.md n°3). Les parts consultées reflètent la RÉALITÉ des consommateurs
/// (<c>{Autre}</c> par le CHECK, <c>{Frais}</c> par B4 e-reporting B2C marge — seule Adjudication reste
/// exclue), pas l'activation du vertical (qui ne gouverne que l'éditeur) ; l'analyseur lui-même reste
/// générique sur l'ensemble consulté qu'on lui fournit.
/// </summary>
public sealed class MappingConsistencyAnalyzerTests
{
    private static readonly MappingPart[] AutreAndFrais = { MappingPart.Autre, MappingPart.Frais };
    private static readonly IReadOnlySet<MappingPart> AutreSet = new HashSet<MappingPart> { MappingPart.Autre };

    private static readonly IReadOnlySet<MappingPart> AllPartsSet =
        new HashSet<MappingPart> { MappingPart.Adjudication, MappingPart.Frais, MappingPart.Autre };

    private static readonly string[] ObservedA = { "R-A" };
    private static readonly string[] ObservedAB = { "R-A", "R-B" };

    [Fact]
    public void PipelineConsulted_IsAutreAndFrais()
    {
        // Deux consommateurs : CHECK mappe Autre (CheckTvaMapping.LinePart) ; B4 e-reporting B2C marge
        // mappe Frais (B2cMarginAggregatorTenantJob.ResolveMarginAsync). Adjudication reste exclue. Le
        // jeu consulté ne dépend PAS de l'activation du vertical enchères (qui ne gouverne que l'éditeur).
        ConsultedMappingParts.PipelineConsulted().Should().BeEquivalentTo(AutreAndFrais);
    }

    [Fact]
    public void AdjudicationRule_IsDead_PartNotConsulted_UnderPipelineReality()
    {
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Adjudication), Rule("R-A", MappingPart.Autre) },
            consultedParts: ConsultedMappingParts.PipelineConsulted(),
            observed: ObservedA);

        report.DeadRules.Should().ContainSingle();
        var dead = report.DeadRules[0];
        dead.SourceRegimeCode.Should().Be("R-A");
        dead.Part.Should().Be(MappingPart.Adjudication);
        dead.Reasons.Should().Equal(DeadRuleReason.PartNotConsulted);
    }

    [Fact]
    public void FraisRule_IsNotDead_BecauseB4ConsultsFrais()
    {
        // Régression BUG-3 : une règle Frais EST consultée (B4 e-reporting B2C marge) — elle ne doit
        // JAMAIS être marquée morte (le faux « morte » poussait à supprimer une règle indispensable).
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Frais) },
            consultedParts: ConsultedMappingParts.PipelineConsulted(),
            observed: ObservedA);

        report.HasDeadRules.Should().BeFalse();
    }

    [Fact]
    public void Analyzer_IsGeneric_WhenPartIsConsulted_RuleNotDeadForPart()
    {
        // L'analyseur est pur et générique : si on lui DÉCLARE Adjudication consultée (cas futur PIP03b),
        // la règle n'est pas morte pour ce motif. La réalité {Autre} est imposée par le handler, pas ici.
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Adjudication), Rule("R-A", MappingPart.Frais) },
            consultedParts: AllPartsSet,
            observed: ObservedA);

        report.HasDeadRules.Should().BeFalse();
    }

    [Fact]
    public void RegimeNeverObserved_IsFlagged_WhenObservationsExist()
    {
        var report = Analyze(
            rules: new[] { Rule("TYPO-X", MappingPart.Autre) },
            consultedParts: AutreSet,
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
            consultedParts: AutreSet,
            observed: Array.Empty<string>());

        report.HasDeadRules.Should().BeFalse();
    }

    [Fact]
    public void Rule_CanCarry_BothReasons()
    {
        var report = Analyze(
            rules: new[] { Rule("TYPO-X", MappingPart.Adjudication) },
            consultedParts: AutreSet,
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
            consultedParts: AutreSet,
            observed: ObservedA);

        report.DeadRules[0].Reasons.Should().Equal(DeadRuleReason.RegimeNeverObserved);
    }

    [Fact]
    public void NoDeadRules_WhenAllConsultedAndObserved()
    {
        var report = Analyze(
            rules: new[] { Rule("R-A", MappingPart.Autre) },
            consultedParts: AutreSet,
            observed: ObservedA);

        report.HasDeadRules.Should().BeFalse();
        report.IsTableConfigured.Should().BeTrue();
    }

    private static MappingConsistencyReport Analyze(
        IReadOnlyList<MappingRuleConsistencyView> rules,
        IReadOnlySet<MappingPart> consultedParts,
        IReadOnlyCollection<string> observed)
    {
        return MappingConsistencyAnalyzer.Analyze(rules, consultedParts, observed, tableConfigured: true);
    }

    private static MappingRuleConsistencyView Rule(string code, MappingPart part) => new()
    {
        SourceRegimeCode = code,
        Part = part,
    };
}
