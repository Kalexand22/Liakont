namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Double de test minimal d'<see cref="ISignatureProvider"/> piloté par ses <see cref="Capabilities"/>. Il
/// prouve le comportement EXIGÉ par l'abstraction (ADR-0027) : un niveau/une localisation non déclaré(e),
/// ou un webhook reçu par un fournisseur sans le flag <see cref="CompletionTransport.Webhook"/>, retourne
/// un résultat TYPÉ <c>CapabilityNotSupported</c>, JAMAIS une exception. (Les plug-ins concrets — Yousign
/// SIG07, Wacom SIG08 — sont hors périmètre de SIG03.)
/// </summary>
internal sealed class FakeSignatureProvider : ISignatureProvider
{
    private readonly List<string> _calls = [];

    public FakeSignatureProvider(SignatureProviderCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    /// <inheritdoc />
    public SignatureProviderCapabilities Capabilities { get; }

    /// <summary>Journal des appels (preuve d'audit — utile en assertion).</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <inheritdoc />
    public Task<SignatureRequestResult> RequestSignatureAsync(
        SignatureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _calls.Add(nameof(RequestSignatureAsync));

        if (!Capabilities.Supports(request.RequestedMode))
        {
            return Task.FromResult(SignatureRequestResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, request.RequestedMode)));
        }

        if (!Capabilities.Supports(request.RequestedLevel))
        {
            return Task.FromResult(SignatureRequestResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, request.RequestedLevel)));
        }

        // Synchrone (capteur sur place) → complété ; sinon soumis (complétion asynchrone à venir).
        return Task.FromResult(
            Capabilities.CompletionTransport.HasFlag(CompletionTransport.Synchronous)
                ? SignatureRequestResult.Completed("FAKE-SIG-1")
                : SignatureRequestResult.Submitted("FAKE-SIG-1"));
    }

    /// <inheritdoc />
    public Task<SignatureStatus> GetSignatureStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(GetSignatureStatusAsync));
        return Task.FromResult(new SignatureStatus
        {
            ProviderReference = providerReference,
            State = SignatureCompletionState.Pending,
        });
    }

    /// <inheritdoc />
    public Task<SignatureProof> DownloadProofAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(DownloadProofAsync));
        if (!Capabilities.SupportsDocumentHashBinding)
        {
            return Task.FromResult(SignatureProof.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(
                    Capabilities.ProviderName, SignatureCapability.ProofDownload)));
        }

        return Task.FromResult(SignatureProof.Available([1, 2, 3], "application/pdf"));
    }

    /// <inheritdoc />
    public Task<SignatureWebhookResult> HandleWebhookAsync(
        SignatureWebhookContext context,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(HandleWebhookAsync));
        if (!Capabilities.CompletionTransport.HasFlag(CompletionTransport.Webhook))
        {
            return Task.FromResult(SignatureWebhookResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(
                    Capabilities.ProviderName, SignatureCapability.WebhookCompletion)));
        }

        return Task.FromResult(SignatureWebhookResult.Accepted("FAKE-SIG-1"));
    }
}
