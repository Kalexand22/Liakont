namespace Stratum.Modules.Job.Contracts.Services;

public interface ICronPreviewService
{
    CronValidationResult Validate(string cronExpression);

    IReadOnlyList<DateTimeOffset> GetNextOccurrences(string cronExpression, int count = 5);
}

public record CronValidationResult(bool IsValid, string? ErrorMessage = null);
