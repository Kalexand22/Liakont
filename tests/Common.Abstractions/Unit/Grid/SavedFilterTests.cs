namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class SavedFilterTests
{
    [Fact]
    public void RecordShouldPreserveAllProperties()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var group = new FilterGroup(FilterLogic.And, [new FilterCriterion("Name", FilterOperator.Contains, "test")]);
        var created = DateTimeOffset.UtcNow;

        var filter = new SavedFilter(
            id,
            userId,
            "Sales.InvoiceList.Main",
            "High-value invoices",
            group,
            false,
            SharedScope.None,
            created,
            null);

        filter.Id.Should().Be(id);
        filter.UserId.Should().Be(userId);
        filter.GridKey.Should().Be("Sales.InvoiceList.Main");
        filter.Name.Should().Be("High-value invoices");
        filter.FilterGroup.Should().Be(group);
        filter.IsDefault.Should().BeFalse();
        filter.SharedWith.Should().Be(SharedScope.None);
        filter.CreatedAt.Should().Be(created);
        filter.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void RecordShouldSupportWithExpression()
    {
        var filter = CreateFilter();
        var updated = DateTimeOffset.UtcNow;

        var modified = filter with { IsDefault = true, UpdatedAt = updated };

        modified.IsDefault.Should().BeTrue();
        modified.UpdatedAt.Should().Be(updated);
        modified.Name.Should().Be(filter.Name);
    }

    [Fact]
    public void RecordShouldSupportValueEquality()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var group = new FilterGroup(FilterLogic.And, []);
        var ts = DateTimeOffset.UtcNow;

        var a = new SavedFilter(id, userId, "key", "name", group, false, SharedScope.None, ts, null);
        var b = new SavedFilter(id, userId, "key", "name", group, false, SharedScope.None, ts, null);

        a.Should().Be(b);
    }

    [Fact]
    public void SharedScopeShouldSupportBothValues()
    {
        var filter = CreateFilter(shared: SharedScope.None);
        filter.SharedWith.Should().Be(SharedScope.None);

        var shared = filter with { SharedWith = SharedScope.Everyone };
        shared.SharedWith.Should().Be(SharedScope.Everyone);
    }

    [Fact]
    public void GridKeyShouldFollowModulePageGridConvention()
    {
        var filter = CreateFilter(gridKey: "Party.PartyList.Main");

        filter.GridKey.Should().Contain(".");
        filter.GridKey.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void FilterGroupShouldSupportNestedGroups()
    {
        var inner = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("Amount", FilterOperator.GreaterThan, 1000)]);
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Name", FilterOperator.Contains, "test")],
            [inner]);

        var filter = CreateFilter(filterGroup: group);

        filter.FilterGroup.SubGroups.Should().HaveCount(1);
        filter.FilterGroup.Criteria.Should().HaveCount(1);
    }

    [Fact]
    public void DefaultFilterShouldBeExplicitlyMarked()
    {
        var nonDefault = CreateFilter(isDefault: false);
        var defaultFilter = CreateFilter(isDefault: true);

        nonDefault.IsDefault.Should().BeFalse();
        defaultFilter.IsDefault.Should().BeTrue();
    }

    private static SavedFilter CreateFilter(
        string gridKey = "Sales.InvoiceList.Main",
        bool isDefault = false,
        SharedScope shared = SharedScope.None,
        FilterGroup? filterGroup = null)
    {
        var group = filterGroup ?? new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Amount", FilterOperator.GreaterThan, 100)]);

        return new SavedFilter(
            Guid.NewGuid(),
            Guid.NewGuid(),
            gridKey,
            "Test filter",
            group,
            isDefault,
            shared,
            DateTimeOffset.UtcNow,
            null);
    }
}
