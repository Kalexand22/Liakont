namespace Stratum.Common.Abstractions.Validation;

/// <summary>
/// Aggregate result of running one or more validators against an entity.
/// </summary>
public sealed record ValidationResult
{
    private ValidationResult()
    {
    }

    public bool IsValid { get; private init; }

    public IReadOnlyList<ValidationFinding> Findings { get; private init; } = [];

    /// <summary>
    /// Creates a valid result with no findings.
    /// </summary>
    public static ValidationResult Valid() => new() { IsValid = true };

    /// <summary>
    /// Creates a valid result with non-error findings (warnings/info).
    /// Throws if any finding has <see cref="ValidationSeverity.Error"/> severity.
    /// </summary>
    public static ValidationResult Valid(IReadOnlyList<ValidationFinding> findings)
    {
        if (findings.Any(f => f.Severity == ValidationSeverity.Error))
        {
            throw new ArgumentException(
                "A valid result cannot contain error-severity findings.", nameof(findings));
        }

        return new() { IsValid = true, Findings = findings };
    }

    /// <summary>
    /// Creates an invalid result with the specified findings.
    /// Requires at least one finding so that the failure is never opaque.
    /// </summary>
    public static ValidationResult Invalid(IReadOnlyList<ValidationFinding> findings)
    {
        if (findings.Count == 0)
        {
            throw new ArgumentException(
                "An invalid result must have at least one finding.", nameof(findings));
        }

        return new() { IsValid = false, Findings = findings };
    }

    /// <summary>
    /// Creates an invalid result with a single error finding.
    /// </summary>
    public static ValidationResult Invalid(string message, string? field = null, string? code = null) =>
        new()
        {
            IsValid = false,
            Findings =
            [
                new ValidationFinding
                {
                    Severity = ValidationSeverity.Error,
                    Field = field,
                    Message = message,
                    Code = code,
                },
            ],
        };

    /// <summary>
    /// Merges multiple validation results into one.
    /// The merged result is invalid if any individual result is invalid.
    /// An empty sequence yields a valid result (vacuous truth: no validators = nothing invalid).
    /// </summary>
    public static ValidationResult Merge(IEnumerable<ValidationResult> results)
    {
        var allFindings = new List<ValidationFinding>();
        var isValid = true;

        foreach (var result in results)
        {
            if (!result.IsValid)
            {
                isValid = false;
            }

            allFindings.AddRange(result.Findings);
        }

        return new ValidationResult
        {
            IsValid = isValid,
            Findings = allFindings,
        };
    }
}
