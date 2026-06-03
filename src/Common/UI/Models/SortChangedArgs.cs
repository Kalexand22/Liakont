namespace Stratum.Common.UI.Models;

/// <summary>Arguments raised when a SimpleTable sort column or direction changes.</summary>
/// <param name="Column">Identifier of the column being sorted (typically the field name).</param>
/// <param name="Direction">New sort direction.</param>
public sealed record SortChangedArgs(string Column, SortDirection Direction);
