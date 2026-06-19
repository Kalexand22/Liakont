namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Contracts;

/// <summary>Double de test d'<see cref="ISignatureProviderRegistry"/> : résout toujours un fournisseur fixé.</summary>
internal sealed class FakeProviderRegistry : ISignatureProviderRegistry
{
    private readonly ISignatureProvider _provider;

    public FakeProviderRegistry(ISignatureProvider provider)
    {
        _provider = provider;
    }

    public IReadOnlyCollection<string> RegisteredTypes => ["Yousign"];

    public ISignatureProvider Resolve(SignatureProviderAccount account) => _provider;

    public bool IsRegistered(string providerType) => true;
}
