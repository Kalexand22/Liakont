namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.TvaMapping.Domain.CoverageDetection;
using Xunit;

/// <summary>
/// Détection proactive des régimes non mappés (item TVA03, F03 §4.3) testée EN DIRECT : croisement
/// des régimes source observés avec la table du tenant, verdict complet/incomplet, et grain de
/// couverture au CODE avec comparaison EXACTE (INV-012). Analyseur PUR : ne consulte que ses
/// arguments (isolation tenant structurelle, INV-008/010).
/// </summary>
public sealed class MappingCoverageAnalyzerTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    private static ObservedSourceRegime Observed(string code, long occurrences = 1, string? label = null) => new()
    {
        Code = code,
        Label = label,
        Occurrences = occurrences,
        LastSeenAtUtc = SeenAt,
    };

    private static MappingTableSummary Table(params string[] mappedCodes) => new()
    {
        MappingVersion = "tenant-v1",
        IsValidated = true,
        MappedRegimeCodes = mappedCodes,
    };

    [Fact]
    public void Analyze_AllObservedRegimesMapped_VerdictComplete()
    {
        var observed = new[] { Observed("REGIME-A"), Observed("REGIME-B") };
        var table = Table("REGIME-A", "REGIME-B", "REGIME-C");

        var report = MappingCoverageAnalyzer.Analyze(observed, table);

        report.Verdict.Should().Be(MappingCoverageVerdict.Complete);
        report.AbsentRegimes.Should().BeEmpty();
        report.CoveredRegimes.Select(r => r.Code).Should().Equal("REGIME-A", "REGIME-B");
        report.IsTableConfigured.Should().BeTrue();
    }

    [Fact]
    public void Analyze_SomeObservedRegimesUnmapped_VerdictIncomplete_ListsAbsent()
    {
        var observed = new[]
        {
            Observed("REGIME-A", occurrences: 12),
            Observed("REGIME-X", occurrences: 7, label: "Régime inconnu"),
        };
        var table = Table("REGIME-A");

        var report = MappingCoverageAnalyzer.Analyze(observed, table);

        report.Verdict.Should().Be(MappingCoverageVerdict.Incomplete);
        report.CoveredRegimes.Select(r => r.Code).Should().Equal("REGIME-A");
        report.AbsentRegimes.Should().ContainSingle();
        var absent = report.AbsentRegimes.Single();
        absent.Code.Should().Be("REGIME-X");
        absent.Label.Should().Be("Régime inconnu");
        absent.Occurrences.Should().Be(7);
        absent.LastSeenAtUtc.Should().Be(SeenAt);
    }

    [Fact]
    public void Analyze_NoObservedRegimes_VerdictComplete_EmptyLists()
    {
        var report = MappingCoverageAnalyzer.Analyze(Array.Empty<ObservedSourceRegime>(), Table("REGIME-A"));

        report.Verdict.Should().Be(MappingCoverageVerdict.Complete);
        report.CoveredRegimes.Should().BeEmpty();
        report.AbsentRegimes.Should().BeEmpty();
        report.IsTableConfigured.Should().BeTrue();
    }

    [Fact]
    public void Analyze_NoTableConfigured_AllObservedAbsent_VerdictIncomplete()
    {
        var observed = new[] { Observed("REGIME-A"), Observed("REGIME-B") };

        var report = MappingCoverageAnalyzer.Analyze(observed, table: null);

        report.Verdict.Should().Be(MappingCoverageVerdict.Incomplete);
        report.IsTableConfigured.Should().BeFalse();
        report.IsTableValidated.Should().BeFalse();
        report.MappingVersion.Should().BeNull();
        report.AbsentRegimes.Select(r => r.Code).Should().Equal("REGIME-A", "REGIME-B");
        report.CoveredRegimes.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_NoTable_NoObservedRegimes_VerdictComplete()
    {
        var report = MappingCoverageAnalyzer.Analyze(Array.Empty<ObservedSourceRegime>(), table: null);

        report.Verdict.Should().Be(MappingCoverageVerdict.Complete);
        report.IsTableConfigured.Should().BeFalse();
        report.AbsentRegimes.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_CodeMatchingIsOrdinalCaseSensitive()
    {
        // Cohérence avec le matching EXACT du moteur (TvaMapper, INV-011) : une casse différente
        // serait bloquée à l'exécution → on la signale absente plutôt que faussement couverte (INV-012).
        var observed = new[] { Observed("REGIME-A") };
        var table = Table("regime-a");

        var report = MappingCoverageAnalyzer.Analyze(observed, table);

        report.Verdict.Should().Be(MappingCoverageVerdict.Incomplete);
        report.AbsentRegimes.Select(r => r.Code).Should().Equal("REGIME-A");
    }

    [Fact]
    public void Analyze_CodeCoveredAcrossMultipleParts_CountedOnceAsCovered()
    {
        // Une table de régime de la marge a deux règles pour le même code (adjudication + frais) :
        // le code apparaît deux fois dans les codes mappés → couvert une seule fois (INV-012).
        var observed = new[] { Observed("REGIME-MARGE") };
        var table = Table("REGIME-MARGE", "REGIME-MARGE");

        var report = MappingCoverageAnalyzer.Analyze(observed, table);

        report.Verdict.Should().Be(MappingCoverageVerdict.Complete);
        report.CoveredRegimes.Should().ContainSingle().Which.Code.Should().Be("REGIME-MARGE");
    }

    [Fact]
    public void Analyze_ReflectsTableVersionAndValidationState()
    {
        var table = new MappingTableSummary
        {
            MappingVersion = "tenant-v9",
            IsValidated = false,
            MappedRegimeCodes = new[] { "REGIME-A" },
        };

        var report = MappingCoverageAnalyzer.Analyze(new[] { Observed("REGIME-A") }, table);

        report.MappingVersion.Should().Be("tenant-v9");
        report.IsTableValidated.Should().BeFalse();
        report.IsTableConfigured.Should().BeTrue();
    }

    [Fact]
    public void Analyze_NullObservedRegimes_Throws()
    {
        var act = () => MappingCoverageAnalyzer.Analyze(null!, Table("REGIME-A"));

        act.Should().Throw<ArgumentNullException>();
    }
}
