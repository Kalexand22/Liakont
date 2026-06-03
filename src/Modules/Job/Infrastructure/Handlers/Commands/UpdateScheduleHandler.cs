namespace Stratum.Modules.Job.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Commands;

public sealed class UpdateScheduleHandler : IRequestHandler<UpdateScheduleCommand>
{
    private readonly IScheduleUnitOfWorkFactory _uowFactory;

    public UpdateScheduleHandler(IScheduleUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(UpdateScheduleCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var schedule = await uow.GetScheduleByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule is null)
        {
            throw new InvalidOperationException($"Schedule '{request.ScheduleId}' not found.");
        }

        var nameExists = await uow.ExistsByNameAndCompanyAsync(
            request.Name,
            schedule.CompanyId,
            excludeId: schedule.Id,
            ct: cancellationToken);

        if (nameExists)
        {
            throw new InvalidOperationException(
                $"INV-JOB-005: A schedule named '{request.Name}' already exists for this company.");
        }

        CronParser.Validate(request.CronExpression);
        var nextRunAt = CronParser.CalculateNextRun(request.CronExpression, DateTimeOffset.UtcNow);

        schedule.Update(request.Name, request.CronExpression, request.JobType, request.PayloadTemplate, nextRunAt);

        await uow.UpdateScheduleAsync(schedule, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
