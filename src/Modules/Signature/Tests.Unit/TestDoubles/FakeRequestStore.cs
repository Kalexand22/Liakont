namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Domain.Entities;

/// <summary>Double de test de <see cref="ISignatureRequestStore"/> : renvoie une liaison fixée (ou <c>null</c>).</summary>
internal sealed class FakeRequestStore : ISignatureRequestStore
{
    private readonly SignatureRequestLink? _link;

    public FakeRequestStore(SignatureRequestLink? link)
    {
        _link = link;
    }

    public Task RecordAsync(SignatureRequestLink link, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<SignatureRequestLink?> GetByProviderReferenceAsync(
        Guid companyId, string providerType, string providerReference, CancellationToken cancellationToken = default)
    {
        var match = _link is not null
            && _link.CompanyId == companyId
            && string.Equals(_link.ProviderType, providerType, StringComparison.OrdinalIgnoreCase)
            && _link.ProviderReference == providerReference;
        return Task.FromResult(match ? _link : null);
    }
}
