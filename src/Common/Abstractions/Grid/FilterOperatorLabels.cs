namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// French display labels for <see cref="FilterOperator"/> values.
/// </summary>
public static class FilterOperatorLabels
{
    private static readonly Dictionary<FilterOperator, string> Labels = new()
    {
        [FilterOperator.Equals] = "Égal à",
        [FilterOperator.NotEquals] = "Différent de",
        [FilterOperator.Contains] = "Contient",
        [FilterOperator.NotContains] = "Ne contient pas",
        [FilterOperator.StartsWith] = "Commence par",
        [FilterOperator.EndsWith] = "Se termine par",
        [FilterOperator.GreaterThan] = "Supérieur à",
        [FilterOperator.LessThan] = "Inférieur à",
        [FilterOperator.GreaterThanOrEqual] = "Supérieur ou égal à",
        [FilterOperator.LessThanOrEqual] = "Inférieur ou égal à",
        [FilterOperator.Between] = "Entre",
        [FilterOperator.NotBetween] = "Pas entre",
        [FilterOperator.In] = "Parmi",
        [FilterOperator.NotIn] = "Pas parmi",
        [FilterOperator.IsNull] = "Est vide",
        [FilterOperator.IsNotNull] = "Est renseigné",
        [FilterOperator.Before] = "Avant",
        [FilterOperator.After] = "Après",
        [FilterOperator.RelativePeriod] = "Période relative",
    };

    /// <summary>
    /// Returns the French label for the given operator, or the enum name as fallback.
    /// </summary>
    public static string GetLabel(FilterOperator op)
    {
        return Labels.TryGetValue(op, out var label) ? label : op.ToString();
    }
}
