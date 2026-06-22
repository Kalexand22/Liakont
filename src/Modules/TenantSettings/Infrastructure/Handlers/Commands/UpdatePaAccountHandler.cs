namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Met à jour un compte PA du tenant courant (F12-A §4). Une clé API non vide remplace l'ancienne
/// (chiffrée) ; <c>null</c> laisse la clé inchangée. Le clair n'est jamais persisté ni journalisé.
/// </summary>
public sealed class UpdatePaAccountHandler : IRequestHandler<UpdatePaAccountCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly ISecretProtector _secretProtector;
    private readonly TenantSettingsJournal _journal;

    public UpdatePaAccountHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        ISecretProtector secretProtector,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _secretProtector = secretProtector;
        _journal = journal;
    }

    public async Task Handle(UpdatePaAccountCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var environment = TenantSettingsParsing.ParseEnvironment(request.Environment);
        var rotateKey = !string.IsNullOrWhiteSpace(request.ApiKey);
        var rotateClientId = !string.IsNullOrWhiteSpace(request.ClientId);
        var rotateClientSecret = !string.IsNullOrWhiteSpace(request.ClientSecret);
        var rotateTechnicalPassword = !string.IsNullOrWhiteSpace(request.TechnicalPassword);

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var account = await uow.GetPaAccountByIdAsync(request.PaAccountId, companyId, cancellationToken)
                ?? throw new NotFoundException("PaAccount", request.PaAccountId);

            account.UpdateDetails(environment, request.AccountIdentifiers ?? string.Empty);
            if (rotateKey)
            {
                account.SetEncryptedApiKey(_secretProtector.Protect(request.ApiKey!));
            }

            if (rotateClientId)
            {
                account.SetEncryptedClientId(_secretProtector.Protect(request.ClientId!, PaAccountSecretPurposes.ClientId));
            }

            if (rotateClientSecret)
            {
                account.SetEncryptedClientSecret(_secretProtector.Protect(request.ClientSecret!, PaAccountSecretPurposes.ClientSecret));
            }

            if (rotateTechnicalPassword)
            {
                account.SetEncryptedTechnicalPassword(_secretProtector.Protect(request.TechnicalPassword!, PaAccountSecretPurposes.TechnicalPassword));
            }

            await uow.UpdatePaAccountAsync(account, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "PaAccount",
            request.PaAccountId,
            "updated",
            $"Compte PA mis à jour ({environment}).",
            companyId,
            new
            {
                Environment = environment.ToString(),
                ApiKeyRotated = rotateKey,
                ClientIdRotated = rotateClientId,
                ClientSecretRotated = rotateClientSecret,
                TechnicalPasswordRotated = rotateTechnicalPassword,
            },
            cancellationToken);
    }
}
