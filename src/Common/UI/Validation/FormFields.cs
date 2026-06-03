namespace Stratum.Common.UI.Validation;

/// <summary>
/// Centralized helpers used by form pages to validate required fields without
/// hand-rolled <c>if (string.IsNullOrWhiteSpace(...))</c> per field. Returned
/// errors are intended to be surfaced via <c>DeclaredFormPage.GlobalError</c>.
/// </summary>
public static class FormFields
{
    /// <summary>
    /// Validates that every supplied field has a non-blank value.
    /// </summary>
    /// <param name="fields">Pairs of (label, value). Label is shown to the user when the value is blank.</param>
    /// <returns>(true, null) if all fields are populated; otherwise (false, formatted error message).</returns>
    public static (bool IsValid, string? Error) Required(params (string Label, string? Value)[] fields)
    {
        var missing = new List<string>();
        foreach (var (label, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(label);
            }
        }

        return missing.Count == 0
            ? (true, null)
            : (false, $"Champs obligatoires manquants : {string.Join(", ", missing)}.");
    }
}
