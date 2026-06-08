namespace Liakont.Host.Tests.Unit.Pipeline;

using System.Linq;
using FluentAssertions;
using Liakont.Host.Pipeline;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class PipelineRunColumnRegistryTests
{
    private readonly PipelineRunColumnRegistry _registry = new();

    [Fact]
    public void Declares_The_Journal_Columns_With_French_Titles_In_Order()
    {
        var titles = _registry.GetAvailableColumns()
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Title)
            .ToArray();

        titles.Should().Equal(
            "Date",
            "Nature",
            "Déclencheur",
            "Durée",
            "Traités",
            "Validés",
            "En échec",
            "Détail");
    }

    [Fact]
    public void Column_Keys_Match_The_Row_Property_Names_So_Reflective_Sort_And_Search_Resolve()
    {
        var keys = _registry.GetAvailableColumns().Select(c => c.Key);

        keys.Should().Contain(new[]
        {
            nameof(PipelineRunRow.StartedAt),
            nameof(PipelineRunRow.Nature),
            nameof(PipelineRunRow.Trigger),
            nameof(PipelineRunRow.Duration),
            nameof(PipelineRunRow.DocumentsProcessed),
            nameof(PipelineRunRow.DocumentsValidated),
            nameof(PipelineRunRow.DocumentsFailed),
            nameof(PipelineRunRow.Detail),
        });
    }

    [Fact]
    public void Detail_Is_The_Only_Column_Hidden_By_Default()
    {
        var defaultVisible = _registry.GetDefaultVisibleColumns().Select(c => c.Title).ToArray();

        defaultVisible.Should().HaveCount(7);
        defaultVisible.Should().NotContain("Détail");
    }

    [Fact]
    public void Date_Column_Is_Typed_As_Date_And_Counters_As_Numbers()
    {
        _registry.GetColumn(nameof(PipelineRunRow.StartedAt))!.DataType.Should().Be(ColumnDataType.Date);
        _registry.GetColumn(nameof(PipelineRunRow.DocumentsProcessed))!.DataType.Should().Be(ColumnDataType.Number);
        _registry.GetColumn(nameof(PipelineRunRow.DocumentsValidated))!.DataType.Should().Be(ColumnDataType.Number);
        _registry.GetColumn(nameof(PipelineRunRow.DocumentsFailed))!.DataType.Should().Be(ColumnDataType.Number);
    }

    [Fact]
    public void Text_Columns_Are_Searchable_By_Default()
    {
        // GetSearchableFields(null) → fields derived from the default-visible text/number columns.
        var fields = _registry.GetSearchableFields(visibleKeys: null);

        fields.Should().Contain(nameof(PipelineRunRow.Nature));
        fields.Should().Contain(nameof(PipelineRunRow.Trigger));
        fields.Should().Contain(nameof(PipelineRunRow.Duration));
    }
}
