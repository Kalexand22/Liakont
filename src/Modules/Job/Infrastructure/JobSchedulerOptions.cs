namespace Stratum.Modules.Job.Infrastructure;

public sealed class JobSchedulerOptions
{
    public const string SectionName = "JobScheduler";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}
