namespace Stratum.Modules.Job.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Commands;

public sealed class ToggleScheduleHandler : IRequestHandler<ToggleScheduleCommand>
{
    private readonly IScheduleUnitOfWorkFactory _uowFactory;

    public ToggleScheduleHandler(IScheduleUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(ToggleScheduleCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var schedule = await uow.GetScheduleByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule is null)
        {
            throw new InvalidOperationException($"Schedule '{request.ScheduleId}' not found.");
        }

        DateTimeOffset? nextRunAt = !schedule.IsActive
            ? CronParser.CalculateNextRun(schedule.CronExpression, DateTimeOffset.UtcNow)
            : null;

        schedule.Toggle(nextRunAt);

        await uow.UpdateScheduleAsync(schedule, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
