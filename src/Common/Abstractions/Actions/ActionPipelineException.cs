namespace Stratum.Common.Abstractions.Actions;

using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Thrown when the action pipeline produces error-severity findings,
/// blocking the command from executing. Maps to HTTP 400 Bad Request.
/// </summary>
public sealed class ActionPipelineException : DomainException
{
    public ActionPipelineException(IReadOnlyList<ActionFinding> findings)
        : base(FormatMessage(Validate(findings)))
    {
        Findings = findings;
    }

    /// <summary>
    /// The error findings that caused the pipeline to block.
    /// </summary>
    public IReadOnlyList<ActionFinding> Findings { get; }

    private static IReadOnlyList<ActionFinding> Validate(IReadOnlyList<ActionFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
        {
            throw new ArgumentException("At least one finding is required.", nameof(findings));
        }

        return findings;
    }

    private static string FormatMessage(IReadOnlyList<ActionFinding> findings)
    {
        if (findings.Count == 1)
        {
            return findings[0].Message;
        }

        return $"Action pipeline validation failed with {findings.Count} error(s): " +
               string.Join("; ", findings.Select(f => f.Message));
    }
}
