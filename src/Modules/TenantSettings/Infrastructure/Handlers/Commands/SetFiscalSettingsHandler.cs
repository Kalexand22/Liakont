namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Définit (upsert) le paramétrage fiscal du tenant courant (F12-A §3). <c>null</c> est une valeur
/// signifiante (suspension) ; <c>reportingFrequency</c> est stocké opaque (énumération non figée).
/// </summary>
public sealed class SetFiscalSettingsHandler : IRequestHandler<SetFiscalSettingsCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetFiscalSettingsHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetFiscalSettingsCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var operationCategory = TenantSettingsParsing.ParseOperationCategory(request.OperationCategory);

        Guid settingsId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetFiscalSettingsByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var settings = FiscalSettings.Create(companyId, request.VatOnDebits, operationCategory, request.ReportingFrequency);
                await uow.InsertFiscalSettingsAsync(settings, cancellationToken);
                settingsId = settings.Id;
                activityType = "created";
            }
            else
            {
                existing.Update(request.VatOnDebits, operationCategory, request.ReportingFrequency);
                await uow.UpdateFiscalSettingsAsync(existing, cancellationToken);
                settingsId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "FiscalSettings",
            settingsId,
            activityType,
            $"Paramétrage fiscal {activityType}.",
            companyId,
            new
            {
                request.VatOnDebits,
                OperationCategory = operationCategory?.ToString(),
                request.ReportingFrequency,
            },
            cancellationToken);
    }
}
