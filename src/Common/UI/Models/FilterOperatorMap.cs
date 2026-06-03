namespace Stratum.Common.UI.Models;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Maps <see cref="ColumnDataType"/> to the set of <see cref="FilterOperator"/>
/// values applicable for that data type.
/// </summary>
public static class FilterOperatorMap
{
    private static readonly Dictionary<ColumnDataType, IReadOnlyList<FilterOperator>> Map = new()
    {
        [ColumnDataType.Text] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.Contains,
            FilterOperator.NotContains,
            FilterOperator.StartsWith,
            FilterOperator.EndsWith,
            FilterOperator.In,
            FilterOperator.NotIn,
            FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        },
        [ColumnDataType.Number] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.GreaterThan,
            FilterOperator.GreaterThanOrEqual,
            FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual,
            FilterOperator.Between,
            FilterOperator.NotBetween,
            FilterOperator.In,
            FilterOperator.NotIn,
            FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        },
        [ColumnDataType.Date] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.Before,
            FilterOperator.After,
            FilterOperator.GreaterThan,
            FilterOperator.GreaterThanOrEqual,
            FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual,
            FilterOperator.Between,
            FilterOperator.NotBetween,
            FilterOperator.RelativePeriod,
            FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        },
        [ColumnDataType.Boolean] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        },
        [ColumnDataType.Money] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.GreaterThan,
            FilterOperator.GreaterThanOrEqual,
            FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual,
            FilterOperator.Between,
            FilterOperator.NotBetween,
            FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        },
        [ColumnDataType.Enum] = new[]
        {
            FilterOperator.Equals,
            FilterOperator.NotEquals,
            FilterOperator.In,
            FilterOperator.NotIn,
        },
    };

    /// <summary>
    /// Returns the operators applicable for the given column data type.
    /// </summary>
    public static IReadOnlyList<FilterOperator> GetOperators(ColumnDataType dataType)
    {
        return Map.TryGetValue(dataType, out var operators)
            ? operators
            : Array.Empty<FilterOperator>();
    }

    /// <summary>
    /// Checks whether the given operator is valid for the specified data type.
    /// </summary>
    public static bool IsOperatorValid(ColumnDataType dataType, FilterOperator op)
    {
        return Map.TryGetValue(dataType, out var operators) && operators.Contains(op);
    }
}
