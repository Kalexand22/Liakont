namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Suspend ou réactive le tenant courant (F12-A §2).</summary>
public sealed class SetTenantStatusHandler : IRequestHandler<SetTenantStatusCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetTenantStatusHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetTenantStatusCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var target = TenantSettingsParsing.ParseStatus(request.Statut);

        Guid profileId;
        TenantStatus previous;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var profile = await uow.GetTenantProfileByCompanyAsync(companyId, cancellationToken)
                ?? throw new NotFoundException("TenantProfile", companyId);

            previous = profile.Statut;
            if (target == TenantStatus.Suspendu)
            {
                profile.Suspend();
            }
            else
            {
                profile.Reactivate();
            }

            await uow.UpdateTenantProfileAsync(profile, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            profileId = profile.Id;
        }

        await _journal.RecordAsync(
            "TenantProfile",
            profileId,
            "status_changed",
            $"Statut de tenant changé de {previous} à {target}.",
            companyId,
            new { From = previous.ToString(), To = target.ToString() },
            cancellationToken);
    }
}
