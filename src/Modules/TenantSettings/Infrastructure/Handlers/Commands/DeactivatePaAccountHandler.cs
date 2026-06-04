namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Désactive un compte PA du tenant courant (F12-A §4).</summary>
public sealed class DeactivatePaAccountHandler : IRequestHandler<DeactivatePaAccountCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public DeactivatePaAccountHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(DeactivatePaAccountCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var account = await uow.GetPaAccountByIdAsync(request.PaAccountId, companyId, cancellationToken)
                ?? throw new NotFoundException("PaAccount", request.PaAccountId);

            account.Deactivate();
            await uow.UpdatePaAccountAsync(account, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "PaAccount",
            request.PaAccountId,
            "deactivated",
            "Compte PA désactivé.",
            companyId,
            ct: cancellationToken);
    }
}
