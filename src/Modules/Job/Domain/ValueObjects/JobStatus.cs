namespace Stratum.Modules.Job.Domain.ValueObjects;

public sealed class JobStatus
{
    public static readonly JobStatus Pending = new("Pending");
    public static readonly JobStatus Running = new("Running");
    public static readonly JobStatus Completed = new("Completed");
    public static readonly JobStatus Failed = new("Failed");
    public static readonly JobStatus Dead = new("Dead");

    private static readonly Dictionary<string, JobStatus> All = new(StringComparer.OrdinalIgnoreCase)
    {
        [Pending.Value] = Pending,
        [Running.Value] = Running,
        [Completed.Value] = Completed,
        [Failed.Value] = Failed,
        [Dead.Value] = Dead,
    };

    private JobStatus(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static JobStatus From(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !All.TryGetValue(value, out var status))
        {
            throw new ArgumentException(
                $"Invalid job status '{value}'. Must be one of: {string.Join(", ", All.Keys)}.",
                nameof(value));
        }

        return status;
    }

    public static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value) && All.ContainsKey(value);

    public override string ToString() => Value;

    public override bool Equals(object? obj) => obj is JobStatus other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}
