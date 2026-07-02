namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Mapping;
using Liakont.Modules.TvaMapping.Domain.Services;
using Xunit;

/// <summary>
/// Moteur de mapping TVA (item TVA02, F03 §4) testé EN DIRECT : application de la table validée du
/// tenant à (code source, part, flags), production du triplet {catégorie, taux, VATEX} + trace, et
/// blocage sûr par défaut sur régime non couvert ou flags non satisfaits (INV-007). Couvre aussi
/// l'isolation tenant : le moteur ne consulte QUE la table fournie.
/// </summary>
public sealed class TvaMapperTests
{
    private static readonly DateTimeOffset MappedAt = new(2026, 7, 15, 10, 30, 0, TimeSpan.Zero);

    private static MappingRule Fixed(
        string code,
        VatCategory category,
        decimal rate,
        MappingPart part = MappingPart.Adjudication,
        string? vatex = null,
        IReadOnlyDictionary<string, string>? flags = null,
        string? label = null) => new()
        {
            SourceRegimeCode = code,
            Part = part,
            Category = category,
            Vatex = vatex,
            RateMode = RateMode.Fixed,
            RateValue = rate,
            SourceFlags = flags,
            Label = label,
        };

    private static MappingRule Computed(string code, VatCategory category, MappingPart part = MappingPart.Frais) => new()
    {
        SourceRegimeCode = code,
        Part = part,
        Category = category,
        RateMode = RateMode.ComputedFromSource,
        RateValue = null,
    };

    private static MappingTable Table(Guid companyId, params MappingRule[] rules)
        => MappingTable.Create(companyId, "v1", null, null, MappingDefaultBehavior.Block, rules);

    private static MappingTable ValidatedTable(Guid companyId, params MappingRule[] rules)
        => MappingTable.Create(
            companyId, "cmp-v1", "Expert-comptable CMP", new DateOnly(2026, 7, 15), MappingDefaultBehavior.Block, rules);

    private static MappingRequest Request(
        string code,
        MappingPart part = MappingPart.Adjudication,
        IReadOnlyDictionary<string, string>? flags = null)
        => new() { SourceRegimeCode = code, Part = part, SourceFlags = flags };

    [Fact]
    public void Map_FixedStandardRate_ProducesCategoryRateAndTrace()
    {
        // Cas nominal : régime assujetti normal → S / 20 %.
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m, label: "Assujetti 20%"));

        var result = TvaMapper.Map(table, Request("REGIME-5"), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.S);
        result.RateMode.Should().Be(RateMode.Fixed);
        result.Rate.Should().Be(20m);
        result.Vatex.Should().BeNull();
        result.BlockReason.Should().BeNull();
        result.Trace.Should().NotBeNull();
        result.Trace!.Category.Should().Be(VatCategory.S);
        result.Trace.Rate.Should().Be(20m);
        result.Trace.RuleOrdinal.Should().Be(1);
        result.Trace.RuleLabel.Should().Be("Assujetti 20%");
        result.Trace.InputRegimeCode.Should().Be("REGIME-5");
        result.Trace.Part.Should().Be(MappingPart.Adjudication);
        result.Trace.MappingVersion.Should().Be("cmp-v1");
    }

    [Fact]
    public void Map_MargeExoneration_ProducesE_WithVatex()
    {
        // Régime de la marge (F03 §2.3) : adjudication exonérée E / 0 % / VATEX-EU-J.
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-MARGE", VatCategory.E, 0m, MappingPart.Adjudication, vatex: "VATEX-EU-J"));

        var result = TvaMapper.Map(table, Request("REGIME-MARGE"), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.E);
        result.Rate.Should().Be(0m);
        result.Vatex.Should().Be("VATEX-EU-J");
        result.Trace!.Vatex.Should().Be("VATEX-EU-J");
    }

    [Fact]
    public void Map_ReducedRate_ProducesAA()
    {
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-10", VatCategory.AA, 10m));

        var result = TvaMapper.Map(table, Request("REGIME-10"), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.AA);
        result.Rate.Should().Be(10m);
    }

    [Fact]
    public void Map_UnknownRegime_IsBlocked()
    {
        // INV-007 / F03 §4.1 : un régime non couvert bloque, ne devine jamais.
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));

        var result = TvaMapper.Map(table, Request("REGIME-INCONNU"), MappedAt);

        result.IsMapped.Should().BeFalse();
        result.Category.Should().BeNull();
        result.Rate.Should().BeNull();
        result.Vatex.Should().BeNull();
        result.Trace.Should().BeNull();
        result.BlockReason.Should().NotBeNullOrWhiteSpace();
        result.BlockReason.Should().Contain("REGIME-INCONNU");
        result.BlockReason.Should().Contain("bloqué");
        result.BlockReason.Should().Contain("Action opérateur");
    }

    [Fact]
    public void Map_KnownCodeButWrongPart_IsBlocked()
    {
        // Une règle existe pour (REGIME-5, Adjudication) mais pas pour la part Frais → blocage.
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m, MappingPart.Adjudication));

        var result = TvaMapper.Map(table, Request("REGIME-5", MappingPart.Frais), MappedAt);

        result.IsMapped.Should().BeFalse();
        result.BlockReason.Should().Contain("Frais");
    }

    [Fact]
    public void Map_SameCodeDifferentPart_SelectsCorrectRule()
    {
        // Régime de la marge : un même code → adjudication (E) + frais (S). Le moteur sélectionne la
        // règle correspondant à la PART demandée.
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-MARGE", VatCategory.E, 0m, MappingPart.Adjudication, vatex: "VATEX-EU-J"),
            Fixed("REGIME-MARGE", VatCategory.S, 20m, MappingPart.Frais));

        var adjudication = TvaMapper.Map(table, Request("REGIME-MARGE", MappingPart.Adjudication), MappedAt);
        var frais = TvaMapper.Map(table, Request("REGIME-MARGE", MappingPart.Frais), MappedAt);

        adjudication.Category.Should().Be(VatCategory.E);
        adjudication.Rate.Should().Be(0m);
        frais.Category.Should().Be(VatCategory.S);
        frais.Rate.Should().Be(20m);
        frais.Trace!.RuleOrdinal.Should().Be(2);
    }

    [Fact]
    public void Map_RuleWithFlags_AllSatisfied_IsMapped()
    {
        // F03 §3 : le même code peut exiger des flags (RegimeMarge). Flags satisfaits → règle appliquée.
        var flags = new Dictionary<string, string> { ["RegimeMarge"] = "true" };
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-6", VatCategory.E, 0m, MappingPart.Adjudication, vatex: "VATEX-EU-J", flags: flags));

        var docFlags = new Dictionary<string, string> { ["RegimeMarge"] = "true", ["autre"] = "x" };
        var result = TvaMapper.Map(table, Request("REGIME-6", flags: docFlags), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.E);
        result.Vatex.Should().Be("VATEX-EU-J");
    }

    [Fact]
    public void Map_RuleWithFlags_NotSatisfied_IsBlocked()
    {
        // Flag attendu RegimeMarge=true mais le document porte false → la règle ne s'applique pas,
        // le régime tombe sur block (jamais une 2ᵉ règle pour le même (code, part) — INV-003).
        var flags = new Dictionary<string, string> { ["RegimeMarge"] = "true" };
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-6", VatCategory.E, 0m, MappingPart.Adjudication, vatex: "VATEX-EU-J", flags: flags));

        var docFlags = new Dictionary<string, string> { ["RegimeMarge"] = "false" };
        var result = TvaMapper.Map(table, Request("REGIME-6", flags: docFlags), MappedAt);

        result.IsMapped.Should().BeFalse();
        result.BlockReason.Should().Contain("flags");
        result.BlockReason.Should().Contain("RegimeMarge=true");
    }

    [Fact]
    public void Map_RuleWithFlags_DocumentHasNoFlags_IsBlocked()
    {
        var flags = new Dictionary<string, string> { ["RegimeMarge"] = "true" };
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-6", VatCategory.E, 0m, MappingPart.Adjudication, vatex: "VATEX-EU-J", flags: flags));

        var result = TvaMapper.Map(table, Request("REGIME-6", flags: null), MappedAt);

        result.IsMapped.Should().BeFalse();
    }

    [Fact]
    public void Map_RuleWithoutFlags_DocumentExtraFlagsIgnored_IsMapped()
    {
        // Une règle sans flag s'applique inconditionnellement ; les flags du document sont ignorés.
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));
        var docFlags = new Dictionary<string, string> { ["RegimeMarge"] = "true" };

        var result = TvaMapper.Map(table, Request("REGIME-5", flags: docFlags), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.S);
    }

    [Fact]
    public void Map_ComputedRateRule_LeavesRateNullWithComputedMode()
    {
        // F03 §4.1 : taux des frais calculé depuis la source. Le moteur signale le mode ; la valeur
        // numérique est résolue en aval (pipeline) à partir des montants de la ligne.
        var table = ValidatedTable(Guid.NewGuid(), Computed("REGIME-FRAIS", VatCategory.S, MappingPart.Frais));

        var result = TvaMapper.Map(table, Request("REGIME-FRAIS", MappingPart.Frais), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.S);
        result.RateMode.Should().Be(RateMode.ComputedFromSource);
        result.Rate.Should().BeNull();
        result.Trace!.RateMode.Should().Be(RateMode.ComputedFromSource);
        result.Trace.Rate.Should().BeNull();
    }

    [Fact]
    public void Map_ValidatedTable_TraceCarriesValidationIdentity()
    {
        var table = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));

        var trace = TvaMapper.Map(table, Request("REGIME-5"), MappedAt).Trace!;

        trace.IsValidated.Should().BeTrue();
        trace.ValidatedBy.Should().Be("Expert-comptable CMP");
        trace.ValidatedDate.Should().Be(new DateOnly(2026, 7, 15));
        trace.MappedAt.Should().Be(MappedAt);
    }

    [Fact]
    public void Map_NonValidatedTable_StillMaps_TraceFlaggedNonValidee()
    {
        // Une table « NON VALIDÉE » est mappable (dev/démo) ; la trace porte l'état pour le garde-fou
        // d'envoi en production (PIP01/TVA04, INV-006).
        var table = Table(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));

        var result = TvaMapper.Map(table, Request("REGIME-5"), MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Trace!.IsValidated.Should().BeFalse();
        result.Trace.ValidatedBy.Should().BeNull();
    }

    [Fact]
    public void Map_TenantIsolation_UsesOnlyProvidedTable()
    {
        // Deux tenants, deux tables différentes : le moteur, sans état, ne consulte QUE la table
        // fournie (INV-008). Le même code source produit un résultat tenant-spécifique.
        var tableA = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));
        var tableB = ValidatedTable(Guid.NewGuid(), Fixed("REGIME-9", VatCategory.O, 0m));

        var aOnFive = TvaMapper.Map(tableA, Request("REGIME-5"), MappedAt);
        var bOnFive = TvaMapper.Map(tableB, Request("REGIME-5"), MappedAt);
        var bOnNine = TvaMapper.Map(tableB, Request("REGIME-9"), MappedAt);

        aOnFive.IsMapped.Should().BeTrue();
        aOnFive.Category.Should().Be(VatCategory.S);
        bOnFive.IsMapped.Should().BeFalse("REGIME-5 n'existe pas dans la table du tenant B");
        bOnNine.IsMapped.Should().BeTrue();
        bOnNine.Category.Should().Be(VatCategory.O);
    }

    [Fact]
    public void Map_NullTable_Throws()
    {
        var act = () => TvaMapper.Map(null!, Request("REGIME-5"), MappedAt);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Map_NullRequest_Throws()
    {
        var table = Table(Guid.NewGuid(), Fixed("REGIME-5", VatCategory.S, 20m));
        var act = () => TvaMapper.Map(table, null!, MappedAt);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Map_EmptyTable_BlocksEveryRegime()
    {
        var table = Table(Guid.NewGuid());

        var result = TvaMapper.Map(table, Request("REGIME-5"), MappedAt);

        result.IsMapped.Should().BeFalse();
    }

    [Fact]
    public void Map_FraisOfUnmappedRegime_IsBlocked()
    {
        // Pas de joker (INV-011, F03 §3 « régime par régime ») : la part frais d'un régime sans règle
        // (code, Frais) explicite est bloquée, jamais couverte implicitement. REGIME-5 a une règle
        // frais explicite ; REGIME-6 (frais) n'en a pas → block.
        var table = ValidatedTable(
            Guid.NewGuid(),
            Fixed("REGIME-5", VatCategory.S, 20m, MappingPart.Adjudication),
            Fixed("REGIME-5", VatCategory.S, 20m, MappingPart.Frais));

        var result = TvaMapper.Map(table, Request("REGIME-6", MappingPart.Frais), MappedAt);

        result.IsMapped.Should().BeFalse();
    }
}
