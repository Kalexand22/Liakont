namespace Stratum.Modules.Job.Infrastructure;

public sealed class JobWorkerOptions
{
    public const string SectionName = "JobWorker";

    /// <summary>
    /// Interval between polling cycles when no jobs are available.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of jobs to process per polling cycle.
    /// Each job is acquired, executed, and committed in its own transaction.
    /// </summary>
    public int BatchSize { get; set; } = 1;
}
