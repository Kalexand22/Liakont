namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Définit (upsert) l'activation du vertical enchères du tenant courant (décision opérateur D4, lot
/// FIX03). Mêmes garanties que les autres mutations de paramétrage : tenant-scopé par
/// <see cref="ICompanyFilter"/>, journalisé APRÈS commit (un échec d'audit n'échoue jamais la
/// transaction métier — INV-AUDIT-002).
/// </summary>
public sealed class SetAuctionVerticalActivationHandler : IRequestHandler<SetAuctionVerticalActivationCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetAuctionVerticalActivationHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetAuctionVerticalActivationCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        Guid settingsId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetAuctionVerticalSettingsByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var settings = AuctionVerticalSettings.Create(companyId, request.Enabled);
                await uow.InsertAuctionVerticalSettingsAsync(settings, cancellationToken);
                settingsId = settings.Id;
                activityType = "created";
            }
            else
            {
                existing.Update(request.Enabled);
                await uow.UpdateAuctionVerticalSettingsAsync(existing, cancellationToken);
                settingsId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "AuctionVerticalSettings",
            settingsId,
            activityType,
            $"Activation du vertical enchères {activityType} ({(request.Enabled ? "activé" : "désactivé")}).",
            companyId,
            new { request.Enabled },
            cancellationToken);
    }
}
