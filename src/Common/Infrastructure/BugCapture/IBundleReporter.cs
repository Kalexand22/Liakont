namespace Stratum.Common.Infrastructure.BugCapture;

/// <summary>
/// Abstraction for reporting a captured bug/feature bundle.
/// Implementations may write to disk, create a GitHub Issue, etc.
/// </summary>
public interface IBundleReporter
{
    Task<string> ReportAsync(CaptureBundle bundle);
}
