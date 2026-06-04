namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Définit (upsert) la planification d'extraction du tenant courant (F12-A §5).</summary>
public sealed class SetExtractionScheduleHandler : IRequestHandler<SetExtractionScheduleCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetExtractionScheduleHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetExtractionScheduleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        Guid scheduleId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetExtractionScheduleByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var schedule = ExtractionSchedule.Create(companyId, request.Hours, request.CatchUpOnStart);
                await uow.InsertExtractionScheduleAsync(schedule, cancellationToken);
                scheduleId = schedule.Id;
                activityType = "created";
            }
            else
            {
                existing.Update(request.Hours, request.CatchUpOnStart);
                await uow.UpdateExtractionScheduleAsync(existing, cancellationToken);
                scheduleId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "ExtractionSchedule",
            scheduleId,
            activityType,
            $"Planification d'extraction {activityType}.",
            companyId,
            new { HoursCount = request.Hours.Count, request.CatchUpOnStart },
            cancellationToken);
    }
}
