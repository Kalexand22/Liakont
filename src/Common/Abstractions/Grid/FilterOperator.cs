namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Comparison operators available for grid filter criteria.
/// </summary>
public enum FilterOperator
{
    /// <summary>Field value equals the filter value.</summary>
    Equals,

    /// <summary>Field value does not equal the filter value.</summary>
    NotEquals,

    /// <summary>Field value contains the filter value (text only).</summary>
    Contains,

    /// <summary>Field value starts with the filter value (text only).</summary>
    StartsWith,

    /// <summary>Field value is greater than the filter value.</summary>
    GreaterThan,

    /// <summary>Field value is less than the filter value.</summary>
    LessThan,

    /// <summary>Field value is between Value and ValueEnd (inclusive).</summary>
    Between,

    /// <summary>Field value is in a set of values.</summary>
    In,

    /// <summary>Field value is null.</summary>
    IsNull,

    /// <summary>Field value is not null.</summary>
    IsNotNull,

    /// <summary>Field value does not contain the filter value (text only).</summary>
    NotContains,

    /// <summary>Field value ends with the filter value (text only).</summary>
    EndsWith,

    /// <summary>Field value is not in a set of values.</summary>
    NotIn,

    /// <summary>Field value is greater than or equal to the filter value.</summary>
    GreaterThanOrEqual,

    /// <summary>Field value is less than or equal to the filter value.</summary>
    LessThanOrEqual,

    /// <summary>Field value is not between Value and ValueEnd.</summary>
    NotBetween,

    /// <summary>Field value is before the filter value (date/datetime, exclusive).</summary>
    Before,

    /// <summary>Field value is after the filter value (date/datetime, exclusive).</summary>
    After,

    /// <summary>Field value falls within a relative date period (e.g. Today, Last7Days).</summary>
    RelativePeriod,
}
