namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Result returned by each action step and by the pipeline as a whole.
/// </summary>
public sealed record ActionResult
{
    private ActionResult()
    {
    }

    public bool IsSuccess { get; private init; }

    public IReadOnlyList<ActionFinding> Findings { get; private init; } = [];

    public IReadOnlyDictionary<string, object?> ModifiedValues { get; private init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Creates a successful result with no findings.
    /// </summary>
    public static ActionResult Success() => new() { IsSuccess = true };

    /// <summary>
    /// Creates a successful result with informational or warning findings.
    /// </summary>
    public static ActionResult Success(IReadOnlyList<ActionFinding> findings) =>
        new() { IsSuccess = true, Findings = findings };

    /// <summary>
    /// Creates a failure result with the specified findings.
    /// </summary>
    public static ActionResult Failure(IReadOnlyList<ActionFinding> findings) =>
        new() { IsSuccess = false, Findings = findings };

    /// <summary>
    /// Creates a failure result with a single error finding.
    /// </summary>
    public static ActionResult Failure(string field, string message, string? code = null) =>
        new()
        {
            IsSuccess = false,
            Findings =
            [
                new ActionFinding
                {
                    Severity = ActionFindingSeverity.Error,
                    Field = field,
                    Message = message,
                    Code = code,
                },
            ],
        };
}
