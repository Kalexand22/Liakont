namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Base class for column registries that provides fluent column registration
/// and property path resolution for related-table columns.
/// </summary>
/// <typeparam name="TItem">The DTO type displayed in the grid.</typeparam>
public abstract class ColumnRegistryBase<TItem> : IColumnRegistry<TItem>
{
    private readonly List<ColumnDefinition> _columns = [];
    private readonly Dictionary<string, ColumnDefinition> _columnsByKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    /// <inheritdoc />
    public IReadOnlyList<ColumnDefinition> GetAvailableColumns()
    {
        EnsureInitialized();
        return _columns.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<ColumnDefinition>> GetColumnsByCategory()
    {
        EnsureInitialized();
        return _columns
            .GroupBy(c => c.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ColumnDefinition>)g.OrderBy(c => c.SortOrder).ToList().AsReadOnly());
    }

    /// <inheritdoc />
    public IReadOnlyList<ColumnDefinition> GetDefaultVisibleColumns()
    {
        EnsureInitialized();
        return _columns.Where(c => c.DefaultVisible).OrderBy(c => c.SortOrder).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public ColumnDefinition? GetColumn(string key)
    {
        EnsureInitialized();
        return _columnsByKey.GetValueOrDefault(key);
    }

    /// <summary>
    /// Returns the property paths that should be included in full-text search
    /// based on the given set of visible column keys.
    /// For Text/Enum/Number/Money base columns: returns the property path directly.
    /// For DisplayColumns: returns the declared SearchableFields.
    /// For RelatedColumns with Text type: returns the property path.
    /// Date and Boolean columns are skipped.
    /// </summary>
    public IReadOnlyList<string> GetSearchableFields(IReadOnlyList<string>? visibleKeys)
    {
        EnsureInitialized();

        var keys = visibleKeys ?? GetDefaultVisibleColumns().Select(c => c.Key).ToList();
        var fields = new List<string>();

        foreach (var key in keys)
        {
            var col = GetColumn(key);
            if (col is null)
            {
                continue;
            }

            if (col.IsRelatedEntity && col.SearchableFields is { Count: > 0 })
            {
                // Display column: use declared searchable sub-fields
                fields.AddRange(col.SearchableFields);
            }
            else if (col.IsRelatedEntity)
            {
                // Display column without searchable fields → skip (can't search raw navigation property)
            }
            else if (col.DataType is ColumnDataType.Text or ColumnDataType.Enum or ColumnDataType.Number or ColumnDataType.Money)
            {
                // Base or related text/numeric column: searchable by property path.
                // Numeric columns are CAST to text at SQL level by FullTextSearchSqlBuilder.
                fields.Add(col.Property);
            }
        }

        return fields;
    }

    /// <summary>
    /// Override this method to register columns using the fluent builder methods.
    /// Called once on first access (lazy initialization).
    /// </summary>
    protected abstract void Configure();

    /// <summary>
    /// Registers a column on the base table (Category = "Main").
    /// </summary>
    /// <param name="property">Property name on <typeparamref name="TItem"/> (e.g. "LegalName").</param>
    /// <param name="title">Human-readable column title.</param>
    /// <param name="sourceTable">Logical table name (e.g. "Party").</param>
    /// <param name="dataType">Column data type.</param>
    /// <param name="defaultVisible">Whether visible by default.</param>
    /// <param name="sortOrder">Display order within the "Main" category.</param>
    /// <param name="allowedValues">
    /// Optional fixed list of allowed values for an enum-typed column. When provided,
    /// the inline column filter renders a multi-select using these values instead
    /// of a free-text input.
    /// </param>
    protected void Column(
        string property,
        string title,
        string sourceTable,
        ColumnDataType dataType = ColumnDataType.Text,
        bool defaultVisible = true,
        int sortOrder = 0,
        IReadOnlyList<string>? allowedValues = null,
        EntityReference? entityReference = null)
    {
        AddColumn(new ColumnDefinition(
            Key: property,
            Title: title,
            SourceTable: sourceTable,
            Property: property,
            DataType: dataType,
            DefaultVisible: defaultVisible,
            Category: "Main",
            SortOrder: sortOrder,
            AllowedValues: allowedValues,
            EntityReference: entityReference));
    }

    /// <summary>
    /// Registers a column whose values come from a CLR enum. The column is
    /// flagged as <see cref="ColumnDataType.Enum"/> and the inline filter
    /// renders a multi-select populated from <see cref="Enum.GetNames(Type)"/>.
    /// </summary>
    /// <typeparam name="TEnum">The enum type backing the column.</typeparam>
    protected void EnumColumn<TEnum>(
        string property,
        string title,
        string sourceTable,
        bool defaultVisible = true,
        int sortOrder = 0)
        where TEnum : struct, Enum
    {
        AddColumn(new ColumnDefinition(
            Key: property,
            Title: title,
            SourceTable: sourceTable,
            Property: property,
            DataType: ColumnDataType.Enum,
            DefaultVisible: defaultVisible,
            Category: "Main",
            SortOrder: sortOrder,
            AllowedValues: Enum.GetNames<TEnum>()));
    }

    /// <summary>
    /// Registers a related-table column using dot-notation property path.
    /// The category is set to the related table name by convention.
    /// </summary>
    /// <param name="propertyPath">
    /// Dot-notation path on <typeparamref name="TItem"/> (e.g. "Customer.Name", "Customer.City").
    /// </param>
    /// <param name="title">Human-readable column title.</param>
    /// <param name="relatedTable">Related table name, used as both SourceTable and Category.</param>
    /// <param name="dataType">Column data type.</param>
    /// <param name="defaultVisible">Whether visible by default.</param>
    /// <param name="sortOrder">Display order within the related table's category.</param>
    protected void RelatedColumn(
        string propertyPath,
        string title,
        string relatedTable,
        ColumnDataType dataType = ColumnDataType.Text,
        bool defaultVisible = false,
        int sortOrder = 0)
    {
        AddColumn(new ColumnDefinition(
            Key: propertyPath,
            Title: title,
            SourceTable: relatedTable,
            Property: propertyPath,
            DataType: dataType,
            DefaultVisible: defaultVisible,
            Category: relatedTable,
            SortOrder: sortOrder));
    }

    /// <summary>
    /// Registers a related-entity display column that renders the full entity
    /// using its <see cref="Stratum.Common.Abstractions.Display.IDisplayTemplate{TEntity}"/>.
    /// The grid resolves the navigation property and formats via DisplayTemplateRegistry.
    /// </summary>
    /// <param name="navigationProperty">
    /// Navigation property on <typeparamref name="TItem"/> (e.g. "Party", "Customer").
    /// </param>
    /// <param name="title">Human-readable column title.</param>
    /// <param name="relatedEntityType">CLR type of the related entity (e.g. typeof(PartyDto)).</param>
    /// <param name="relatedTable">Related table name, used as both SourceTable and Category.</param>
    /// <param name="defaultVisible">Whether visible by default.</param>
    /// <param name="sortOrder">Display order within the related table's category.</param>
    /// <param name="searchableFields">
    /// Property paths (e.g. "Party.LegalName", "Party.TradeName") that full-text search
    /// should target when this display column is visible. These correspond to the fields
    /// used by the entity's DisplayTemplate.
    /// </param>
    protected void DisplayColumn(
        string navigationProperty,
        string title,
        Type relatedEntityType,
        string relatedTable,
        bool defaultVisible = true,
        int sortOrder = 0,
        IReadOnlyList<string>? searchableFields = null)
    {
        AddColumn(new ColumnDefinition(
            Key: navigationProperty,
            Title: title,
            SourceTable: relatedTable,
            Property: navigationProperty,
            DataType: ColumnDataType.Text,
            DefaultVisible: defaultVisible,
            Category: relatedTable,
            SortOrder: sortOrder,
            IsRelatedEntity: true,
            RelatedEntityType: relatedEntityType,
            SearchableFields: searchableFields));
    }

    private void AddColumn(ColumnDefinition column)
    {
        if (_columnsByKey.ContainsKey(column.Key))
        {
            throw new InvalidOperationException(
                $"Column with key '{column.Key}' is already registered in {GetType().Name}.");
        }

        _columns.Add(column);
        _columnsByKey[column.Key] = column;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Configure();
        _initialized = true;
    }
}
