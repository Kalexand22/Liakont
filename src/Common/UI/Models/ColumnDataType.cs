namespace Stratum.Common.UI.Models;

/// <summary>
/// Data type of a grid column, used to drive formatting and filter behavior.
/// </summary>
public enum ColumnDataType
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Numeric (integer or decimal).</summary>
    Number,

    /// <summary>Date or date-time.</summary>
    Date,

    /// <summary>Boolean (true/false).</summary>
    Boolean,

    /// <summary>Monetary amount (formatted with currency).</summary>
    Money,

    /// <summary>Enumeration value (status, type, etc.).</summary>
    Enum,
}
