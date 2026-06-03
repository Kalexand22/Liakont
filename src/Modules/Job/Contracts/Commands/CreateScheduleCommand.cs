namespace Stratum.Modules.Job.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record CreateScheduleCommand : ICommand<Guid>
{
    public required string Name { get; init; }

    public required string CronExpression { get; init; }

    public required string JobType { get; init; }

    public string PayloadTemplate { get; init; } = "{}";

    public required Guid CompanyId { get; init; }
}
