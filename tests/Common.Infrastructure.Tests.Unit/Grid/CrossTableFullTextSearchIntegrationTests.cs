namespace Stratum.Common.Infrastructure.Tests.Unit.Grid;

using Dapper;
using FluentAssertions;
using Stratum.Common.Infrastructure.GridPreferences;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Integration tests verifying the chain: ColumnRegistry.GetSearchableFields()
/// → FullTextSearchSqlBuilder.Build() → correct cross-table SQL.
/// </summary>
public sealed class CrossTableFullTextSearchIntegrationTests
{
    /// <summary>
    /// A DisplayColumn with SearchableFields should expand into individual fields
    /// that FullTextSearchSqlBuilder resolves to correct cross-table SQL columns.
    /// </summary>
    [Fact]
    public void DisplayColumnSearchableFieldsShouldProduceCorrectCrossTableSql()
    {
        // Arrange: column registry with a DisplayColumn (like QuoteList's "Party" column)
        var registry = new QuoteLikeColumnRegistry();
        var visibleKeys = new List<string> { "QuoteNumber", "Party" };

        // Act 1: Get searchable fields from registry (expanding DisplayColumn)
        var searchableFields = registry.GetSearchableFields(visibleKeys);

        // Assert 1: DisplayColumn "Party" expands to its searchable fields
        searchableFields.Should().Contain("QuoteNumber");
        searchableFields.Should().Contain("Party.LegalName");
        searchableFields.Should().Contain("Party.TradeName");
        searchableFields.Should().NotContain("Party"); // DisplayColumn key itself excluded

        // Act 2: Feed expanded fields into FullTextSearchSqlBuilder
        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["QuoteNumber"] = "q.quote_number",
            ["Party.LegalName"] = "p.legal_name",
            ["Party.TradeName"] = "p.trade_name",
        };
        var builder = new FullTextSearchSqlBuilder(fieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("Acme", searchableFields, parameters, out var skipped);

        // Assert 2: SQL targets all three columns (base + cross-table)
        sql.Should().NotBeNull();
        sql.Should().Contain("CAST(q.quote_number AS TEXT) ILIKE @fts");
        sql.Should().Contain("CAST(p.legal_name AS TEXT) ILIKE @fts");
        sql.Should().Contain("CAST(p.trade_name AS TEXT) ILIKE @fts");
        sql.Should().Contain(" OR ");
        parameters.Get<string>("fts").Should().Be("%Acme%");
        skipped.Should().BeEmpty();
    }

    /// <summary>
    /// When only base-table columns are visible, no cross-table SQL is generated.
    /// </summary>
    [Fact]
    public void BaseTableOnlyColumnsShouldNotProduceCrossTableSql()
    {
        var registry = new QuoteLikeColumnRegistry();

        var searchableFields = registry.GetSearchableFields(["QuoteNumber", "Status"]);

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["QuoteNumber"] = "q.quote_number",
            ["Status"] = "q.status",
        };
        var builder = new FullTextSearchSqlBuilder(fieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("test", searchableFields, parameters, out var skipped);

        sql.Should().NotBeNull();
        sql.Should().Contain("q.quote_number");
        sql.Should().Contain("q.status");
        sql.Should().NotContain("p."); // No party table columns
        skipped.Should().BeEmpty();
    }

    /// <summary>
    /// Boolean and Date columns in the registry should not appear in search SQL.
    /// </summary>
    [Fact]
    public void NonSearchableColumnTypesShouldBeExcludedFromSql()
    {
        var registry = new QuoteLikeColumnRegistry();

        // Include all columns including boolean and date
        var searchableFields = registry.GetSearchableFields(
            ["QuoteNumber", "IsConfirmed", "CreatedAt", "Status"]);

        // Boolean and Date columns should be filtered out by GetSearchableFields
        searchableFields.Should().Contain("QuoteNumber");
        searchableFields.Should().Contain("Status"); // Enum → included
        searchableFields.Should().NotContain("IsConfirmed"); // Boolean → excluded
        searchableFields.Should().NotContain("CreatedAt"); // Date → excluded
    }

    // ── Test registry mimicking QuoteList patterns ──────────────────
    private sealed record QuoteLikeItem(
        int Id,
        string QuoteNumber,
        string Status,
        bool IsConfirmed,
        DateTime CreatedAt,
        object? Party);

    private sealed class QuoteLikeColumnRegistry : ColumnRegistryBase<QuoteLikeItem>
    {
        protected override void Configure()
        {
            Column("QuoteNumber", "N° devis", "Quote", defaultVisible: true, sortOrder: 1);
            Column("Status", "Statut", "Quote", dataType: ColumnDataType.Enum, defaultVisible: true, sortOrder: 2);
            Column("IsConfirmed", "Confirmé", "Quote", dataType: ColumnDataType.Boolean, defaultVisible: true, sortOrder: 3);
            Column("CreatedAt", "Créé le", "Quote", dataType: ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
            DisplayColumn("Party", "Client", typeof(object), "Party", defaultVisible: true, sortOrder: 5, searchableFields: ["Party.LegalName", "Party.TradeName"]);
        }
    }
}
