namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ColumnRegistryBase{TItem}"/>.
/// Covers column registration, category grouping, default visibility,
/// key lookup, and error handling.
/// </summary>
public sealed class ColumnRegistryBaseTests
{
    // ── GetAvailableColumns ─────────────────────────────────────────────
    [Fact]
    public void GetAvailableColumnsShouldReturnAllColumnsWhenRegistryIsConfigured()
    {
        var registry = new TestRegistry();

        var columns = registry.GetAvailableColumns();

        columns.Should().HaveCount(5);
    }

    [Fact]
    public void GetAvailableColumnsShouldReturnEmptyListWhenNoColumnsRegistered()
    {
        var registry = new EmptyRegistry();

        var columns = registry.GetAvailableColumns();

        columns.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableColumnsShouldIncludeBaseTableColumns()
    {
        var registry = new TestRegistry();

        var columns = registry.GetAvailableColumns();

        columns.Should().Contain(c => c.Key == "Name" && c.Category == "Main" && c.SourceTable == "TestItem");
        columns.Should().Contain(c => c.Key == "Amount" && c.DataType == ColumnDataType.Money);
    }

    [Fact]
    public void GetAvailableColumnsShouldIncludeRelatedTableColumns()
    {
        var registry = new TestRegistry();

        var columns = registry.GetAvailableColumns();

        columns.Should().Contain(c => c.Key == "Customer.Name" && c.Category == "Customer" && c.SourceTable == "Customer");
        columns.Should().Contain(c => c.Key == "Customer.City" && c.Property == "Customer.City");
    }

    // ── GetColumnsByCategory ────────────────────────────────────────────
    [Fact]
    public void GetColumnsByCategoryShouldGroupByCategory()
    {
        var registry = new TestRegistry();

        var grouped = registry.GetColumnsByCategory();

        grouped.Should().ContainKey("Main");
        grouped.Should().ContainKey("Customer");
        grouped["Main"].Should().HaveCount(3);
        grouped["Customer"].Should().HaveCount(2);
    }

    [Fact]
    public void GetColumnsByCategoryShouldSortBySortOrderWithinCategory()
    {
        var registry = new TestRegistry();

        var grouped = registry.GetColumnsByCategory();

        var mainColumns = grouped["Main"];
        mainColumns[0].Key.Should().Be("Name");
        mainColumns[1].Key.Should().Be("Amount");
        mainColumns[2].Key.Should().Be("IsActive");
    }

    [Fact]
    public void GetColumnsByCategoryShouldReturnEmptyDictionaryWhenNoColumns()
    {
        var registry = new EmptyRegistry();

        var grouped = registry.GetColumnsByCategory();

        grouped.Should().BeEmpty();
    }

    // ── GetDefaultVisibleColumns ────────────────────────────────────────
    [Fact]
    public void GetDefaultVisibleColumnsShouldReturnOnlyDefaultVisible()
    {
        var registry = new TestRegistry();

        var visible = registry.GetDefaultVisibleColumns();

        visible.Should().HaveCount(3);
        visible.Should().OnlyContain(c => c.DefaultVisible);
    }

    [Fact]
    public void GetDefaultVisibleColumnsShouldBeSortedBySortOrder()
    {
        var registry = new TestRegistry();

        var visible = registry.GetDefaultVisibleColumns();

        visible[0].Key.Should().Be("Name");
        visible[1].Key.Should().Be("Customer.Name");
        visible[2].Key.Should().Be("Amount");
    }

    // ── GetColumn ───────────────────────────────────────────────────────
    [Fact]
    public void GetColumnShouldReturnColumnWhenKeyExists()
    {
        var registry = new TestRegistry();

        var column = registry.GetColumn("Amount");

        column.Should().NotBeNull();
        column!.Title.Should().Be("Amount");
        column.DataType.Should().Be(ColumnDataType.Money);
    }

    [Fact]
    public void GetColumnShouldReturnColumnWhenRelatedKeyExists()
    {
        var registry = new TestRegistry();

        var column = registry.GetColumn("Customer.Name");

        column.Should().NotBeNull();
        column!.Title.Should().Be("Customer Name");
        column.Category.Should().Be("Customer");
    }

    [Fact]
    public void GetColumnShouldReturnNullWhenKeyNotFound()
    {
        var registry = new TestRegistry();

        var column = registry.GetColumn("NonExistent");

        column.Should().BeNull();
    }

    [Fact]
    public void GetColumnShouldIgnoreCaseWhenLookingUpKey()
    {
        var registry = new TestRegistry();

        var column = registry.GetColumn("name");

        column.Should().NotBeNull();
        column!.Key.Should().Be("Name");
    }

    // ── Duplicate key detection ─────────────────────────────────────────
    [Fact]
    public void ConfigureShouldThrowInvalidOperationExceptionWhenDuplicateKeyRegistered()
    {
        var registry = new DuplicateKeyRegistry();

        var act = () => registry.GetAvailableColumns();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Name'*already registered*");
    }

    // ── Lazy initialization ─────────────────────────────────────────────
    [Fact]
    public void GetAvailableColumnsShouldReturnConsistentResultsAcrossMultipleCalls()
    {
        var registry = new TestRegistry();

        var first = registry.GetAvailableColumns();
        var second = registry.GetAvailableColumns();

        first.Should().BeEquivalentTo(second);
        first.Should().HaveCount(5);
    }

    // ── DisplayColumn (related entity) ─────────────────────────────────
    [Fact]
    public void DisplayColumnShouldSetIsRelatedEntityAndRelatedEntityType()
    {
        var registry = new DisplayColumnRegistry();

        var col = registry.GetColumn("Customer");

        col.Should().NotBeNull();
        col!.IsRelatedEntity.Should().BeTrue();
        col.RelatedEntityType.Should().Be<CustomerDto>();
        col.Category.Should().Be("Customer");
        col.DataType.Should().Be(ColumnDataType.Text);
    }

    [Fact]
    public void RegularColumnShouldHaveIsRelatedEntityFalse()
    {
        var registry = new TestRegistry();

        var col = registry.GetColumn("Name");

        col.Should().NotBeNull();
        col!.IsRelatedEntity.Should().BeFalse();
        col.RelatedEntityType.Should().BeNull();
    }

    [Fact]
    public void RelatedColumnShouldHaveIsRelatedEntityFalse()
    {
        var registry = new TestRegistry();

        var col = registry.GetColumn("Customer.Name");

        col.Should().NotBeNull();
        col!.IsRelatedEntity.Should().BeFalse();
        col.RelatedEntityType.Should().BeNull();
    }

    [Fact]
    public void DisplayColumnShouldAppearInDefaultVisibleColumns()
    {
        var registry = new DisplayColumnRegistry();

        var visible = registry.GetDefaultVisibleColumns();

        visible.Should().Contain(c => c.Key == "Customer" && c.IsRelatedEntity);
    }

    // ── GetSearchableFields ────────────────────────────────────────────
    [Fact]
    public void GetSearchableFieldsShouldIncludeTextColumns()
    {
        var registry = new TestRegistry();

        var fields = registry.GetSearchableFields(["Name", "Amount", "IsActive"]);

        fields.Should().Contain("Name"); // Text
        fields.Should().Contain("Amount"); // Money → included
        fields.Should().NotContain("IsActive"); // Boolean → excluded
    }

    [Fact]
    public void GetSearchableFieldsShouldIncludeRelatedTextColumns()
    {
        var registry = new TestRegistry();

        var fields = registry.GetSearchableFields(["Name", "Customer.Name", "Customer.City"]);

        fields.Should().Contain("Customer.Name");
        fields.Should().Contain("Customer.City");
    }

    [Fact]
    public void GetSearchableFieldsShouldExpandDisplayColumnSearchableFields()
    {
        var registry = new SearchableDisplayColumnRegistry();

        var fields = registry.GetSearchableFields(["Name", "Customer"]);

        fields.Should().Contain("Name");
        fields.Should().Contain("Customer.Name");
        fields.Should().Contain("Customer.City");
        fields.Should().NotContain("Customer"); // Display column key is NOT included directly
    }

    [Fact]
    public void GetSearchableFieldsShouldExcludeDisplayColumnWithoutSearchableFields()
    {
        var registry = new DisplayColumnRegistry();

        var fields = registry.GetSearchableFields(["Name", "Customer"]);

        fields.Should().Contain("Name");

        // DisplayColumn "Customer" has no searchableFields declared → excluded
        fields.Should().NotContain("Customer");
    }

    [Fact]
    public void GetSearchableFieldsShouldUseDefaultColumnsWhenNullKeys()
    {
        var registry = new TestRegistry();

        var fields = registry.GetSearchableFields(null);

        // Default visible: Name (Text), Amount (Money), Customer.Name (Text)
        fields.Should().Contain("Name");
        fields.Should().Contain("Amount");
        fields.Should().Contain("Customer.Name");
    }

    [Fact]
    public void GetSearchableFieldsShouldSkipUnknownKeys()
    {
        var registry = new TestRegistry();

        var fields = registry.GetSearchableFields(["Name", "Unknown"]);

        fields.Should().ContainSingle().Which.Should().Be("Name");
    }

    [Fact]
    public void GetSearchableFieldsShouldExcludeDateAndBooleanColumns()
    {
        var registry = new TestRegistry();

        // IsActive = Boolean, implicitly no Date column in TestRegistry
        var fields = registry.GetSearchableFields(["IsActive"]);

        fields.Should().BeEmpty();
    }

    // ── ColumnDefinition record semantics ───────────────────────────────
    [Fact]
    public void ColumnDefinitionShouldSupportValueEquality()
    {
        var a = new ColumnDefinition("Key", "Title", "Table", "Prop", ColumnDataType.Text, true, "Main", 10);
        var b = new ColumnDefinition("Key", "Title", "Table", "Prop", ColumnDataType.Text, true, "Main", 10);

        a.Should().Be(b);
    }

    [Fact]
    public void ColumnDefinitionShouldSupportWithExpression()
    {
        var original = new ColumnDefinition("Key", "Title", "Table", "Prop", ColumnDataType.Text, true, "Main", 10);

        var modified = original with { DefaultVisible = false };

        modified.DefaultVisible.Should().BeFalse();
        modified.Key.Should().Be("Key");
    }

    // ── Test helpers ────────────────────────────────────────────────────
    private sealed record TestItemDto(string Name, decimal Amount, bool IsActive, string CustomerName, string CustomerCity);

    private sealed class TestRegistry : ColumnRegistryBase<TestItemDto>
    {
        protected override void Configure()
        {
            Column("Name", "Name", "TestItem", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            Column("Amount", "Amount", "TestItem", ColumnDataType.Money, defaultVisible: true, sortOrder: 20);
            Column("IsActive", "Active", "TestItem", ColumnDataType.Boolean, defaultVisible: false, sortOrder: 30);

            RelatedColumn("Customer.Name", "Customer Name", "Customer", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            RelatedColumn("Customer.City", "Customer City", "Customer", ColumnDataType.Text, defaultVisible: false, sortOrder: 20);
        }
    }

    private sealed class EmptyRegistry : ColumnRegistryBase<TestItemDto>
    {
        protected override void Configure()
        {
        }
    }

    private sealed class DuplicateKeyRegistry : ColumnRegistryBase<TestItemDto>
    {
        protected override void Configure()
        {
            Column("Name", "Name 1", "TestItem");
            Column("Name", "Name 2", "TestItem");
        }
    }

    private sealed record CustomerDto(string Name, string City);

    private sealed record DisplayTestItemDto(string Name, CustomerDto? Customer);

    private sealed class DisplayColumnRegistry : ColumnRegistryBase<DisplayTestItemDto>
    {
        protected override void Configure()
        {
            Column("Name", "Name", "TestItem", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            DisplayColumn("Customer", "Customer", typeof(CustomerDto), "Customer", defaultVisible: true, sortOrder: 10);
        }
    }

    private sealed class SearchableDisplayColumnRegistry : ColumnRegistryBase<DisplayTestItemDto>
    {
        protected override void Configure()
        {
            Column("Name", "Name", "TestItem", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            DisplayColumn(
                "Customer",
                "Customer",
                typeof(CustomerDto),
                "Customer",
                defaultVisible: true,
                sortOrder: 10,
                searchableFields: ["Customer.Name", "Customer.City"]);
        }
    }
}
