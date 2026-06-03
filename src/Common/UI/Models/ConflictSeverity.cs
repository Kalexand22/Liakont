namespace Stratum.Common.UI.Models;

/// <summary>
/// Severity for <c>ConflictAlertBanner</c> (UIC03).
/// Two levels by spec: a recoverable warning vs. a blocking critical conflict.
/// </summary>
public enum ConflictSeverity
{
    /// <summary>Amber tone — over-allocation or soft constraint, can be resolved.</summary>
    Warning,

    /// <summary>Red tone — hard conflict, blocks submission until resolved.</summary>
    Critical,
}
