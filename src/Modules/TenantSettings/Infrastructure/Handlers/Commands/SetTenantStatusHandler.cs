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
        // Société : explicite (chemin console d'administration d'instance, OPS03 — l'acteur opérateur
        // porte le company_id de SON tenant) ou contexte courant. Pas de garde create-only ici : la
        // commande ne fait qu'UPDATE le profil de cette société dans la base du tenant CIBLE
        // (database-per-tenant) — un companyId qui n'y correspond à rien donne NotFound, jamais une
        // écriture cross-tenant.
        var companyId = request.CompanyId ?? _companyFilter.GetRequiredCompanyId();
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
