namespace Liakont.Modules.Transmission.Tests.Unit.TestDoubles;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique factice d'<see cref="IPaClient"/> pour un type de plug-in donné — sert à prouver la
/// résolution par registre (acceptance PAA01 : « résolution plug-in par registre, testée avec un
/// type factice »).
/// </summary>
internal sealed class StubPaClientFactory : IPaClientFactory
{
    private readonly PaCapabilities _capabilities;

    public StubPaClientFactory(string paType, PaCapabilities? capabilities = null)
    {
        PaType = paType;
        _capabilities = capabilities ?? new PaCapabilities { PaName = paType };
    }

    /// <inheritdoc />
    public string PaType { get; }

    /// <summary>Dernier compte passé à <see cref="Create"/> (assertion de propagation).</summary>
    public PaAccountDescriptor? LastAccount { get; private set; }

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        LastAccount = account;
        return new StubPaClient(_capabilities);
    }
}
