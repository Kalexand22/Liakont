namespace Stratum.Common.UI.Models;

/// <summary>
/// Metadata for a single grid column. Used by the column registry and column chooser
/// to describe available columns, including related-table columns.
/// </summary>
/// <param name="Key">
/// Unique key within the registry (e.g. "LegalName", "Customer.Name").
/// For related-table columns, uses dot-notation property paths.
/// </param>
/// <param name="Title">Human-readable column header text.</param>
/// <param name="SourceTable">
/// Logical table name this column originates from (e.g. "Invoice", "Customer").
/// </param>
/// <param name="Property">
/// Property path on the grid item type (e.g. "LegalName", "Customer.Name").
/// Matches the <paramref name="Key"/> by convention.
/// </param>
/// <param name="DataType">Data type for formatting and filter behavior.</param>
/// <param name="DefaultVisible">Whether this column is shown by default when no user preference exists.</param>
/// <param name="Category">
/// Grouping category for the column chooser UI.
/// Convention: "Main" for base-table columns, table name for related columns
/// (e.g. "Customer", "Product").
/// </param>
/// <param name="SortOrder">
/// Display order within its category. Lower values appear first.
/// </param>
public sealed record ColumnDefinition(
    string Key,
    string Title,
    string SourceTable,
    string Property,
    ColumnDataType DataType,
    bool DefaultVisible,
    string Category,
    int SortOrder,
    bool IsRelatedEntity = false,
    Type? RelatedEntityType = null,
    IReadOnlyList<string>? SearchableFields = null,
    AggregateFunc AggregateFunc = AggregateFunc.None,
    string? AggregateFormat = null,
    IReadOnlyList<string>? AllowedValues = null,
    EntityReference? EntityReference = null);
