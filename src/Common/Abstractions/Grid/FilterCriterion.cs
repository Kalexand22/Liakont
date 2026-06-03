namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// A single filter condition on a grid column.
/// </summary>
/// <param name="Field">
/// Property path to filter on. Supports dot-notation for related tables
/// (e.g. "Customer.City").
/// </param>
/// <param name="Operator">Comparison operator to apply.</param>
/// <param name="Value">
/// Filter value. Null for <see cref="FilterOperator.IsNull"/> and
/// <see cref="FilterOperator.IsNotNull"/>.
/// </param>
/// <param name="ValueEnd">
/// End value for <see cref="FilterOperator.Between"/> ranges. Null for other operators.
/// </param>
public sealed record FilterCriterion(
    string Field,
    FilterOperator Operator,
    object? Value,
    object? ValueEnd = null);
