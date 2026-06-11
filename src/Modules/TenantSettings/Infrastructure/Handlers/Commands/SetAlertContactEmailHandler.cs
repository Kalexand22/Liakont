namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Met à jour le SEUL e-mail de contact d'alerte du tenant courant (F12-A §2 / F12 §5.3), sans toucher au
/// reste du profil. Le profil doit exister (le SIREN est sa clé fonctionnelle) : sans profil, l'opération est
/// refusée plutôt que d'en créer un sans données légales. L'adresse n'est jamais journalisée en clair
/// (seul « renseigné »/« retiré » l'est).
/// </summary>
public sealed class SetAlertContactEmailHandler : IRequestHandler<SetAlertContactEmailCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetAlertContactEmailHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetAlertContactEmailCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        Guid profileId;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetTenantProfileByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                throw new NotFoundException("TenantProfile", companyId);
            }

            existing.SetAlertContactEmail(request.ContactEmailAlerte);
            await uow.UpdateTenantProfileAsync(existing, cancellationToken);
            profileId = existing.Id;

            await uow.CommitAsync(cancellationToken);
        }

        // L'adresse elle-même n'est PAS journalisée (CLAUDE.md n°10) : seul l'état renseigné/retiré l'est.
        var hasContact = !string.IsNullOrWhiteSpace(request.ContactEmailAlerte);
        var description = hasContact
            ? "Contact d'alerte du tenant renseigné."
            : "Contact d'alerte du tenant retiré.";
        await _journal.RecordAsync(
            "TenantProfile",
            profileId,
            "updated",
            description,
            companyId,
            new { ContactConfigured = hasContact },
            cancellationToken);
    }
}
