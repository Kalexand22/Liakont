namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Crée ou met à jour le profil du tenant courant (F12-A §2). Le SIREN est immuable une fois posé.</summary>
public sealed class SaveTenantProfileHandler : IRequestHandler<SaveTenantProfileCommand, Guid>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SaveTenantProfileHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task<Guid> Handle(SaveTenantProfileCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var address = TenantAddress.Create(request.Street, request.PostalCode, request.City, request.Country);

        Guid profileId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetTenantProfileByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var profile = TenantProfile.Create(companyId, request.Siren, request.RaisonSociale, address, request.ContactEmailAlerte);
                await uow.InsertTenantProfileAsync(profile, cancellationToken);
                profileId = profile.Id;
                activityType = "created";
            }
            else
            {
                if (!string.Equals(existing.Siren, request.Siren?.Trim(), StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "INV-TENANTSETTINGS-001 : le SIREN est la clé fonctionnelle du tenant et ne peut pas être modifié.");
                }

                existing.UpdateDetails(request.RaisonSociale, address, request.ContactEmailAlerte);
                await uow.UpdateTenantProfileAsync(existing, cancellationToken);
                profileId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "TenantProfile",
            profileId,
            activityType,
            $"Profil de tenant {activityType} (SIREN {request.Siren}).",
            companyId,
            new { request.Siren, request.RaisonSociale },
            cancellationToken);

        return profileId;
    }
}
