namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Ajoute un compte Plateforme Agréée au tenant courant (F12-A §4). La clé API reçue en clair est
/// chiffrée immédiatement (Data Protection) ; le clair n'est jamais persisté ni journalisé (CLAUDE.md n°10).
/// </summary>
public sealed class AddPaAccountHandler : IRequestHandler<AddPaAccountCommand, Guid>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly ISecretProtector _secretProtector;
    private readonly TenantSettingsJournal _journal;

    public AddPaAccountHandler(
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

    public async Task<Guid> Handle(AddPaAccountCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var environment = TenantSettingsParsing.ParseEnvironment(request.Environment);

        var encryptedApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? null
            : _secretProtector.Protect(request.ApiKey);
        var encryptedClientId = string.IsNullOrWhiteSpace(request.ClientId)
            ? null
            : _secretProtector.Protect(request.ClientId, PaAccountSecretPurposes.ClientId);
        var encryptedClientSecret = string.IsNullOrWhiteSpace(request.ClientSecret)
            ? null
            : _secretProtector.Protect(request.ClientSecret, PaAccountSecretPurposes.ClientSecret);

        var account = PaAccount.Create(
            companyId,
            request.PluginType,
            environment,
            request.AccountIdentifiers ?? string.Empty,
            encryptedApiKey,
            encryptedClientId,
            encryptedClientSecret);

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            await uow.InsertPaAccountAsync(account, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }

        await _journal.RecordAsync(
            "PaAccount",
            account.Id,
            "created",
            $"Compte PA ajouté ({request.PluginType} / {environment}).",
            companyId,
            new
            {
                request.PluginType,
                Environment = environment.ToString(),
                HasApiKey = encryptedApiKey is not null,
                HasClientId = encryptedClientId is not null,
                HasClientSecret = encryptedClientSecret is not null,
            },
            cancellationToken);

        return account.Id;
    }
}
