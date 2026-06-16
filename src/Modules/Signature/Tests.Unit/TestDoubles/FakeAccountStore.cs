namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;

/// <summary>Double de test de <see cref="ISignatureAccountStore"/> : renvoie un descripteur de compte fixé.</summary>
internal sealed class FakeAccountStore : ISignatureAccountStore
{
    private readonly SignatureProviderAccount? _account;

    public FakeAccountStore(SignatureProviderAccount? account)
    {
        _account = account;
    }

    public Task<SignatureProviderAccount?> GetActiveAccountAsync(
        Guid companyId, string providerType, CancellationToken cancellationToken = default) =>
        Task.FromResult(_account);

    public Task UpsertAsync(
        Guid companyId,
        string providerType,
        string environment,
        string accountIdentifiers,
        string encryptedApiKey,
        string encryptedWebhookSecret,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
