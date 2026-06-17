namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Fournisseur de signature SUR PLACE (capteur Wacom — ADR-0030 ; F17 §6). Plug-in concret d'ADR-0027 piloté
/// EXCLUSIVEMENT par ses <see cref="Capabilities"/>, jamais par un <c>if (provider is …)</c>. Conformément à
/// ADR-0030 :
/// <list type="bullet">
///   <item><see cref="SignatureProviderCapabilities.SupportedLevels"/> = <c>{ SES }</c> au départ — AES
///   seulement après audit du procédé (art. 26 c) ; jamais AES/QES par défaut (INV-ONSITE-8) ;</item>
///   <item><see cref="SignatureProviderCapabilities.SupportsBiometricTemplateMatching"/> = <c>false</c> : on
///   capture le tracé comme preuve, AUCUN gabarit n'est dérivé de la FSS (INV-ONSITE-10, RGPD sobre) ;</item>
///   <item>complétion <see cref="CompletionTransport.Synchronous"/> : la capture est postée au proxy
///   <c>OnSiteCapture</c> (le travail réel — binding, identité, WORM — vit dans le proxy, pas ici).</item>
/// </list>
/// La preuve est rapatriée en WORM via <c>Archive.Contracts</c> (jamais par le plug-in) :
/// <see cref="DownloadProofAsync"/> retourne donc <c>NotSupported</c>. Aucun webhook (pas de flag Webhook).
/// </summary>
internal sealed class OnSiteSignatureProvider : ISignatureProvider
{
    /// <summary>Clé de registre du plug-in (insensible à la casse).</summary>
    public const string ProviderTypeKey = "Wacom";

    /// <inheritdoc />
    public SignatureProviderCapabilities Capabilities { get; } = new()
    {
        ProviderName = "Wacom (signature sur place)",
        Mode = SignatureMode.OnSite,
        CompletionTransport = CompletionTransport.Synchronous,
        SupportedLevels = SignatureLevel.SES,
        SupportsSignerIdentityVerification = false,
        SupportsDocumentHashBinding = true,
        SupportsBiometricCapture = true,
        SupportsBiometricTemplateMatching = false,
        SupportsProofDownload = false,
        MaxDocumentSizeBytes = null,
    };

    /// <inheritdoc />
    public Task<SignatureRequestResult> RequestSignatureAsync(
        SignatureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        // La capture est initiée côté poste (client Wacom) puis postée au proxy OnSiteCapture : la demande est
        // « soumise », sa complétion effective passe par le proxy (binding + journal append-only), pas par ce
        // plug-in. La référence est le document (tenant-scopé).
        return Task.FromResult(SignatureRequestResult.Submitted(request.DocumentId));
    }

    /// <inheritdoc />
    public Task<SignatureStatus> GetSignatureStatusAsync(
        string providerReference, CancellationToken cancellationToken = default)
    {
        // Pas de polling fournisseur pour le sur place (CompletionTransport.Synchronous) : l'état effectif est
        // porté par le journal de preuve / le workflow DocumentApproval, pas par ce plug-in.
        return Task.FromResult(new SignatureStatus
        {
            ProviderReference = providerReference,
            State = SignatureCompletionState.Pending,
        });
    }

    /// <inheritdoc />
    public Task<SignatureProof> DownloadProofAsync(
        string providerReference, CancellationToken cancellationToken = default)
    {
        // Le rapatriement de la preuve passe par Archive.Contracts (coffre WORM), JAMAIS par le plug-in
        // (ADR-0030 §3 ; CLAUDE.md n°6) → capacité non offerte, résultat typé (jamais d'exception).
        return Task.FromResult(SignatureProof.NotSupported(
            SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.ProofDownload)));
    }

    /// <inheritdoc />
    public Task<SignatureWebhookResult> HandleWebhookAsync(
        SignatureWebhookContext context, CancellationToken cancellationToken = default)
    {
        // Aucun webhook pour le sur place (pas de flag CompletionTransport.Webhook) → résultat typé NotSupported.
        return Task.FromResult(SignatureWebhookResult.NotSupported(
            SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.WebhookCompletion)));
    }
}
