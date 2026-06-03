namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class FilterChipProjectorTests
{
    [Fact]
    public void Project_OrdersGlobalSearchFirst_ThenFlatRootCriteria_DF08_GFI16()
    {
        // GFI16: AddSimpleFilter now writes into AdvancedFilter's root AND,
        // so after adding a simple filter and then assigning AdvancedFilter
        // we keep the flat-append semantics via the public setter — the final
        // chip order is [GlobalSearch, Simple, Simple] and each root criterion
        // is editable as a simple chip.
        var state = new GridFilterState { GlobalSearch = "police" };
        state.AddSimpleFilter(
            new FilterCriterion("Service", FilterOperator.Equals, "finances"));
        state.AddSimpleFilter(
            new FilterCriterion("Montant", FilterOperator.GreaterThan, 5000));

        var chips = FilterChipProjector.Project(state);

        chips.Should().HaveCount(3);
        chips[0].Source.Should().Be(FilterSource.GlobalSearch);
        chips[1].Source.Should().Be(FilterSource.Simple);
        chips[1].Label.Should().Contain("Service");
        chips[2].Source.Should().Be(FilterSource.Simple);
        chips[2].Label.Should().Contain("Montant");
    }

    [Fact]
    public void Project_GlobalSearch_ProducesSearchChip()
    {
        var state = new GridFilterState { GlobalSearch = "police" };

        var chips = FilterChipProjector.Project(state);

        chips.Should().ContainSingle();
        var chip = chips[0];
        chip.Label.Should().Contain("police");
        chip.Source.Should().Be(FilterSource.GlobalSearch);
        chip.Criterion.Should().BeNull();
        chip.Group.Should().BeNull();
        chip.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void Project_NoGlobalSearch_NoChip()
    {
        var chips = FilterChipProjector.Project(new GridFilterState());

        chips.Should().BeEmpty();
    }

    [Fact]
    public void Project_WhitespaceGlobalSearch_NoChip()
    {
        var state = new GridFilterState { GlobalSearch = "   " };

        var chips = FilterChipProjector.Project(state);

        chips.Should().BeEmpty();
    }

    [Fact]
    public void Project_SimpleFilter_PreservesCreationOrder()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("B", FilterOperator.Equals, "2"));
        state.AddSimpleFilter(new FilterCriterion("A", FilterOperator.Equals, "1"));

        var chips = FilterChipProjector.Project(state);

        chips.Should().HaveCount(2);
        chips[0].Label.Should().Contain("B");
        chips[1].Label.Should().Contain("A");
    }

    [Fact]
    public void Project_SimpleFilter_SetsSourceAndCriterion()
    {
        var criterion = new FilterCriterion("Service", FilterOperator.Equals, "finances");
        var state = new GridFilterState();
        state.AddSimpleFilter(criterion);

        var chips = FilterChipProjector.Project(state);

        var chip = chips.Should().ContainSingle().Subject;
        chip.Source.Should().Be(FilterSource.Simple);
        chip.Criterion.Should().Be(criterion);

        // GFI16: the chip now carries the owning root group so the remove
        // handler can rebuild it, even for simple chips.
        chip.Group.Should().NotBeNull();
        chip.Group!.Criteria.Should().ContainSingle().Which.Should().Be(criterion);
    }

    [Fact]
    public void Project_AdvancedSingleCriterion_ExplicitSimpleChip_DF05_GFI16()
    {
        // GFI16: a flat single-criterion root projects as a simple chip so the
        // user can edit it from the same popover that created it (regardless of
        // whether it originated in the simple builder or the advanced builder).
        var criterion = new FilterCriterion("Montant", FilterOperator.GreaterThan, 5000);
        var group = new FilterGroup(FilterLogic.And, [criterion]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        var chip = chips.Should().ContainSingle().Subject;
        chip.Source.Should().Be(FilterSource.Simple);
        chip.Criterion.Should().Be(criterion);
        chip.Group.Should().Be(group);
        chip.Label.Should().Contain("Montant");
    }

    [Fact]
    public void Project_AdvancedFlatGroup_IndividualSimpleChips_DF05_GFI16()
    {
        var c1 = new FilterCriterion("A", FilterOperator.Equals, "1");
        var c2 = new FilterCriterion("B", FilterOperator.GreaterThan, 10);
        var group = new FilterGroup(FilterLogic.And, [c1, c2]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        chips.Should().HaveCount(2);
        chips[0].Source.Should().Be(FilterSource.Simple);
        chips[0].Criterion.Should().Be(c1);
        chips[0].Group.Should().Be(group);
        chips[1].Source.Should().Be(FilterSource.Simple);
        chips[1].Criterion.Should().Be(c2);
        chips[1].Group.Should().Be(group);
    }

    [Fact]
    public void Project_AdvancedAndRootWithSubGroups_SimpleChipsPlusSummary_GFI16()
    {
        // GFI16: an AND root with sub-groups now shows an editable simple chip
        // for each root criterion AND a summary chip for the sub-group tree.
        // The summary count covers only the sub-group criteria so the badge
        // reflects what the advanced builder is actually responsible for.
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("C", FilterOperator.Equals, "3"),
                new FilterCriterion("D", FilterOperator.Equals, "4"),
            ]);
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("A", FilterOperator.Equals, "1")],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        chips.Should().HaveCount(2);
        chips[0].Source.Should().Be(FilterSource.Simple);
        chips[0].Criterion!.Field.Should().Be("A");
        chips[1].Source.Should().Be(FilterSource.Advanced);
        chips[1].Criterion.Should().BeNull();
        chips[1].Group.Should().Be(group);
        chips[1].Label.Should().Be("Filtres avancés (2 critères)");
    }

    [Fact]
    public void Project_AdvancedOrRoot_SummaryChipOnly_GFI16()
    {
        // GFI16: a flat OR root cannot be decomposed into independent simple
        // chips without flipping semantics on round-trip, so it projects as a
        // single summary chip instead.
        var group = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            ]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        var chip = chips.Should().ContainSingle().Subject;
        chip.Source.Should().Be(FilterSource.Advanced);
        chip.Criterion.Should().BeNull();
        chip.Label.Should().Be("Filtres avancés (2 critères)");
    }

    [Fact]
    public void GetBadgeCount_ExcludesGlobalSearch_DF03()
    {
        var state = new GridFilterState { GlobalSearch = "search" };
        state.AddSimpleFilter(new FilterCriterion("A", FilterOperator.Equals, "1"));

        FilterChipProjector.GetBadgeCount(state).Should().Be(1);
    }

    [Fact]
    public void GetBadgeCount_CountsSimpleAndAdvanced()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("X", FilterOperator.Equals, "a"),
                new FilterCriterion("Y", FilterOperator.Equals, "b"),
            ]);
        var state = new GridFilterState { AdvancedFilter = group };
        state.AddSimpleFilter(new FilterCriterion("Z", FilterOperator.Contains, "c"));

        FilterChipProjector.GetBadgeCount(state).Should().Be(3);
    }

    [Fact]
    public void Label_BooleanTrue_ShowsOui_DF04()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Actif", FilterOperator.Equals, true));

        label.Should().Be("Actif: Oui");
    }

    [Fact]
    public void Label_BooleanFalse_ShowsNon_DF04()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Actif", FilterOperator.Equals, false));

        label.Should().Be("Actif: Non");
    }

    [Fact]
    public void Label_IsNull_ShowsVide()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Notes", FilterOperator.IsNull, null));

        label.Should().Be("Notes: vide");
    }

    [Fact]
    public void Label_IsNotNull_ShowsRenseigne()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Notes", FilterOperator.IsNotNull, null));

        label.Should().Be("Notes: renseigné");
    }

    [Fact]
    public void Label_Contains_ShowsText()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Nom", FilterOperator.Contains, "police"));

        label.Should().Be("Nom contient \"police\"");
    }

    [Fact]
    public void Label_GreaterThanOrEqual_ShowsSymbol()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Priorité", FilterOperator.GreaterThanOrEqual, 50));

        label.Should().Be("Priorité ≥ 50");
    }

    [Fact]
    public void Label_Between_ShowsRange()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Montant", FilterOperator.Between, 100m, 500m));

        label.Should().Be("Montant: 100 → 500");
    }

    [Fact]
    public void Label_InList_ThreeOrLess_ShowsValues()
    {
        var values = new List<string> { "Dupont SA", "Alpha Corp" };
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Client", FilterOperator.In, values));

        label.Should().Be("Client: Dupont SA, Alpha Corp");
    }

    [Fact]
    public void Label_InList_MoreThanThree_ShowsCount()
    {
        var values = new List<string> { "A", "B", "C", "D" };
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Client", FilterOperator.In, values));

        label.Should().Be("Client: 4 sélectionnés");
    }

    [Fact]
    public void Label_NotIn_ThreeOrLess_ShowsValues()
    {
        var values = new List<string> { "X", "Y" };
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Type", FilterOperator.NotIn, values));

        label.Should().Be("Type: pas parmi X, Y");
    }

    [Fact]
    public void Label_NotIn_MoreThanThree_ShowsCount()
    {
        var values = new List<string> { "A", "B", "C", "D" };
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Type", FilterOperator.NotIn, values));

        label.Should().Be("Type: pas parmi 4 valeurs");
    }

    [Fact]
    public void Label_RelativePeriod_ShowsFrenchLabel()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Date", FilterOperator.RelativePeriod, RelativeDatePeriod.ThisMonth));

        label.Should().Be("Date: ce mois");
    }

    [Fact]
    public void Label_RelativePeriod_StringValue_ShowsFrenchLabel()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Date", FilterOperator.RelativePeriod, "Last7Days"));

        label.Should().Be("Date: 7 derniers jours");
    }

    [Fact]
    public void Label_Before_ShowsDate()
    {
        var date = new DateTime(2026, 1, 15);
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Date", FilterOperator.Before, date));

        label.Should().Be("Date: avant 15/01/2026");
    }

    [Fact]
    public void Label_After_ShowsDate()
    {
        var date = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Date", FilterOperator.After, date));

        label.Should().Be("Date: après 01/03/2026");
    }

    [Fact]
    public void Label_EnumEquals_ShowsValue()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Service", FilterOperator.Equals, "finances"));

        label.Should().Be("Service: finances");
    }

    [Fact]
    public void Label_NotBetween_ShowsRange()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Montant", FilterOperator.NotBetween, 100, 500));

        label.Should().Be("Montant: pas entre 100 et 500");
    }

    [Fact]
    public void Label_NotContains_ShowsText()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Nom", FilterOperator.NotContains, "test"));

        label.Should().Be("Nom ne contient pas \"test\"");
    }

    [Fact]
    public void Label_EndsWith_ShowsText()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Code", FilterOperator.EndsWith, "001"));

        label.Should().Be("Code se termine par \"001\"");
    }

    [Fact]
    public void Label_StartsWith_ShowsText()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Code", FilterOperator.StartsWith, "PRE"));

        label.Should().Be("Code commence par \"PRE\"");
    }

    [Fact]
    public void Project_EmptyState_ReturnsEmpty()
    {
        var chips = FilterChipProjector.Project(new GridFilterState());

        chips.Should().BeEmpty();
    }

    [Fact]
    public void Project_AdvancedEmptyGroup_ProducesNoChip()
    {
        var group = new FilterGroup(FilterLogic.And, []);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        chips.Should().BeEmpty();
    }

    [Fact]
    public void Project_NullState_Throws()
    {
        var act = () => FilterChipProjector.Project(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetBadgeCount_NullState_Throws()
    {
        var act = () => FilterChipProjector.GetBadgeCount(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Label_DecimalWithCents_PreservesFractionalDigits()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Prix", FilterOperator.Equals, 19.99m));

        label.Should().Be("Prix: 19,99");
    }

    [Fact]
    public void Label_EqualsNull_ShowsVide()
    {
        var label = FilterChipProjector.FormatCriterionLabel(
            new FilterCriterion("Référence", FilterOperator.Equals, null));

        label.Should().Be("Référence: vide");
    }

    [Fact]
    public void Project_AdvancedRootEmptyWithNonEmptySubGroup_ProducesSummaryChip()
    {
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("A", FilterOperator.Equals, "1"),
                new FilterCriterion("B", FilterOperator.Equals, "2"),
            ]);
        var group = new FilterGroup(FilterLogic.And, [], [subGroup]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        var chip = chips.Should().ContainSingle().Subject;
        chip.Source.Should().Be(FilterSource.Advanced);
        chip.Label.Should().Be("Filtres avancés (2 critères)");
    }

    [Fact]
    public void Project_AdvancedDeeplyNestedGroups_SummaryCountsSubGroupCriteriaOnly_GFI16()
    {
        // GFI16: root criterion "A" becomes a simple chip. The summary chip
        // covers only the sub-group tree (C + D = 2), not the whole expression.
        var deepGroup = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("D", FilterOperator.Equals, "4")]);
        var midGroup = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("C", FilterOperator.Equals, "3")],
            [deepGroup]);
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("A", FilterOperator.Equals, "1")],
            [midGroup]);
        var state = new GridFilterState { AdvancedFilter = group };

        var chips = FilterChipProjector.Project(state);

        chips.Should().HaveCount(2);
        chips[0].Source.Should().Be(FilterSource.Simple);
        chips[0].Criterion!.Field.Should().Be("A");
        chips[1].Source.Should().Be(FilterSource.Advanced);
        chips[1].Label.Should().Be("Filtres avancés (2 critères)");
    }
}
