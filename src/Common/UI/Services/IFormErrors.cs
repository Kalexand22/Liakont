namespace Stratum.Common.UI.Services;

/// <summary>
/// Tracks field-level validation errors for a form.
/// Populated from domain exceptions or ProblemDetails responses.
/// </summary>
public interface IFormErrors
{
    /// <summary>True when at least one field error is present.</summary>
    bool HasErrors { get; }

    /// <summary>Number of field errors currently tracked.</summary>
    int ErrorCount { get; }

    /// <summary>Returns all field errors as field/message pairs (read-only snapshot).</summary>
    IReadOnlyDictionary<string, string> AllErrors { get; }

    /// <summary>Returns the error message for <paramref name="field"/>, or null if none.</summary>
    string? GetError(string field);

    /// <summary>Removes all field errors.</summary>
    void Clear();

    /// <summary>
    /// Sets a single field error. Does not clear other existing errors.
    /// Use <paramref name="field"/> = "" for a general (non-field) error.
    /// </summary>
    void SetError(string field, string message);

    /// <summary>
    /// Extracts field-level errors from a domain validation exception.
    /// If the exception's <see cref="Exception.Data"/> dictionary contains
    /// string-keyed entries, each is treated as a field error.
    /// Otherwise the message is stored under a general "" key.
    /// </summary>
    void SetFromException(Exception ex);

    /// <summary>
    /// Populates field errors from an ASP.NET ProblemDetails validation errors dictionary.
    /// Each key maps to an array of error messages; only the first message per field is kept.
    /// </summary>
    void SetFromProblemDetails(IDictionary<string, string[]> errors);
}
