namespace Stratum.Modules.Job.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record ToggleScheduleCommand : ICommand
{
    public required Guid ScheduleId { get; init; }
}
