namespace Stratum.Modules.Job.Infrastructure;

using Cronos;

internal static class CronParser
{
    /// <summary>
    /// INV-JOB-004: Validates a cron expression and returns the parsed result.
    /// Throws ArgumentException if the expression is invalid.
    /// </summary>
    public static void Validate(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException(
                "INV-JOB-004: Cron expression must not be empty.", nameof(cronExpression));
        }

        try
        {
            CronExpression.Parse(cronExpression);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException(
                $"INV-JOB-004: Invalid cron expression '{cronExpression}': {ex.Message}",
                nameof(cronExpression),
                ex);
        }
    }

    /// <summary>
    /// Calculates the next occurrence of the cron expression after the given time.
    /// </summary>
    public static DateTimeOffset CalculateNextRun(string cronExpression, DateTimeOffset from)
    {
        var cron = CronExpression.Parse(cronExpression);
        var next = cron.GetNextOccurrence(from.UtcDateTime, inclusive: false);

        if (next is null)
        {
            throw new InvalidOperationException(
                "INV-JOB-004: Cron expression does not produce a next occurrence.");
        }

        return new DateTimeOffset(next.Value, TimeSpan.Zero);
    }
}
