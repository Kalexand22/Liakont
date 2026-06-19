namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Fabrique de test d'un <see cref="FakeSignatureProvider"/> pour un <see cref="ProviderType"/> donné
/// (parité avec <c>StubPaClientFactory</c>). Sert à exercer le registre par clé (résolution, casse,
/// doublons) et la validation au démarrage, sans aucun plug-in concret.
/// </summary>
internal sealed class FakeSignatureProviderFactory : ISignatureProviderFactory
{
    private readonly SignatureProviderCapabilities _capabilities;

    public FakeSignatureProviderFactory(string providerType, SignatureProviderCapabilities? capabilities = null)
    {
        ProviderType = providerType;
        _capabilities = capabilities ?? new SignatureProviderCapabilities { ProviderName = providerType };
    }

    /// <inheritdoc />
    public string ProviderType { get; }

    /// <summary>Dernier compte passé à <see cref="Create"/> (assertion de propagation).</summary>
    public SignatureProviderAccount? LastAccount { get; private set; }

    /// <inheritdoc />
    public ISignatureProvider Create(SignatureProviderAccount account)
    {
        LastAccount = account;
        return new FakeSignatureProvider(_capabilities);
    }
}
