namespace Stratum.Common.UI.Services;

/// <summary>
/// Tracks field-level validation errors for a form.
/// Populated from domain exceptions or ProblemDetails responses.
/// Registered as Transient — each injection site gets its own instance.
/// </summary>
public sealed class FormErrors : IFormErrors
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when at least one field error is present.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Number of field errors currently tracked.</summary>
    public int ErrorCount => _errors.Count;

    /// <summary>Returns all field errors as field/message pairs (read-only snapshot).</summary>
    public IReadOnlyDictionary<string, string> AllErrors => new Dictionary<string, string>(_errors);

    /// <summary>Returns the error message for <paramref name="field"/>, or null if none.</summary>
    public string? GetError(string field) => _errors.GetValueOrDefault(field);

    /// <summary>Removes all field errors.</summary>
    public void Clear() => _errors.Clear();

    /// <summary>
    /// Sets a single field error. Does not clear other existing errors.
    /// Use <paramref name="field"/> = "" for a general (non-field) error.
    /// </summary>
    public void SetError(string field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);
        _errors[field] = message;
    }

    /// <summary>
    /// Extracts field-level errors from a domain validation exception.
    /// If the exception's <see cref="Exception.Data"/> dictionary contains
    /// string-keyed entries, each is treated as a field error.
    /// Otherwise the message is stored under a general "" key.
    /// </summary>
    public void SetFromException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        _errors.Clear();

        var extracted = false;
        foreach (var key in ex.Data.Keys)
        {
            if (key is string fieldName && ex.Data[key] is string message)
            {
                _errors[fieldName] = message;
                extracted = true;
            }
        }

        if (!extracted)
        {
            _errors[string.Empty] = ex.Message;
        }
    }

    /// <summary>
    /// Populates field errors from an ASP.NET ProblemDetails validation errors dictionary.
    /// Each key maps to an array of error messages; only the first message per field is kept.
    /// </summary>
    public void SetFromProblemDetails(IDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors.Clear();

        foreach (var (field, messages) in errors)
        {
            if (messages.Length > 0 && messages[0] is { } msg)
            {
                _errors[field] = msg;
            }
        }
    }
}
