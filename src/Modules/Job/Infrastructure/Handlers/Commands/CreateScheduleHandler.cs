namespace Stratum.Modules.Job.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Commands;
using Stratum.Modules.Job.Domain.Entities;

public sealed class CreateScheduleHandler : IRequestHandler<CreateScheduleCommand, Guid>
{
    private readonly IScheduleUnitOfWorkFactory _uowFactory;

    public CreateScheduleHandler(IScheduleUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateScheduleCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var exists = await uow.ExistsByNameAndCompanyAsync(request.Name, request.CompanyId, ct: cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(
                $"INV-JOB-005: A schedule named '{request.Name}' already exists for this company.");
        }

        CronParser.Validate(request.CronExpression);
        var nextRunAt = CronParser.CalculateNextRun(request.CronExpression, DateTimeOffset.UtcNow);

        var schedule = JobSchedule.Create(
            request.Name,
            request.CronExpression,
            request.JobType,
            request.PayloadTemplate,
            request.CompanyId,
            nextRunAt);

        await uow.InsertScheduleAsync(schedule, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return schedule.Id;
    }
}
