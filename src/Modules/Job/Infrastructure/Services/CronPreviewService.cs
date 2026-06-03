namespace Stratum.Modules.Job.Infrastructure.Services;

using Cronos;
using Stratum.Modules.Job.Contracts.Services;

internal sealed class CronPreviewService : ICronPreviewService
{
    public CronValidationResult Validate(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return new CronValidationResult(false, "L'expression cron est obligatoire.");
        }

        try
        {
            CronExpression.Parse(cronExpression.Trim());
            return new CronValidationResult(true);
        }
        catch (CronFormatException ex)
        {
            return new CronValidationResult(false, $"Expression cron invalide : {ex.Message}");
        }
    }

    public IReadOnlyList<DateTimeOffset> GetNextOccurrences(string cronExpression, int count = 5)
    {
        var validation = Validate(cronExpression);
        if (!validation.IsValid)
        {
            return [];
        }

        var cron = CronExpression.Parse(cronExpression.Trim());
        var results = new List<DateTimeOffset>(count);
        var current = DateTime.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var next = cron.GetNextOccurrence(current, inclusive: false);
            if (next is null)
            {
                break;
            }

            results.Add(new DateTimeOffset(next.Value, TimeSpan.Zero));
            current = next.Value;
        }

        return results;
    }
}
