namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Définit (upsert) les seuils d'alerte de supervision du tenant courant (F12-A §6).</summary>
public sealed class SetAlertThresholdsHandler : IRequestHandler<SetAlertThresholdsCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetAlertThresholdsHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetAlertThresholdsCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        Guid thresholdsId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetAlertThresholdsByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var thresholds = AlertThresholds.Create(
                    companyId,
                    request.AgentSilentHours,
                    request.MissedRunHours,
                    request.PushQueueMaxItems,
                    request.PushQueueMaxAgeHours,
                    request.BlockedDocumentsDays,
                    request.PaRejectionsDays,
                    request.AlertTenantContact);
                await uow.InsertAlertThresholdsAsync(thresholds, cancellationToken);
                thresholdsId = thresholds.Id;
                activityType = "created";
            }
            else
            {
                existing.Update(
                    request.AgentSilentHours,
                    request.MissedRunHours,
                    request.PushQueueMaxItems,
                    request.PushQueueMaxAgeHours,
                    request.BlockedDocumentsDays,
                    request.PaRejectionsDays,
                    request.AlertTenantContact);
                await uow.UpdateAlertThresholdsAsync(existing, cancellationToken);
                thresholdsId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "AlertThresholds",
            thresholdsId,
            activityType,
            $"Seuils d'alerte {activityType}.",
            companyId,
            new { request.AlertTenantContact },
            cancellationToken);
    }
}
