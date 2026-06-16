namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Abstraction d'un fournisseur de signature électronique enfichable (ADR-0027 §1 ; F17 §2). Calquée
/// EXACTEMENT sur <c>IPaClient</c> du module Transmission : le comportement est piloté EXCLUSIVEMENT par
/// les <see cref="Capabilities"/> déclarées, JAMAIS par un <c>if (provider is Yousign)</c>
/// (CLAUDE.md n°6/8/16 ; INV-SIGPROV-1). Quand une capacité ou un niveau manque, l'appel retourne un
/// résultat TYPÉ (état <c>CapabilityNotSupported</c>), JAMAIS une exception et JAMAIS un blocage du
/// produit — la signature est OPTIONNELLE (INV-SIGPROV-5/6). AUCUN type HTTP ne traverse cette interface :
/// la construction du payload propre au fournisseur vit DANS le plug-in (INV-SIGPROV-8, frontière
/// NetArchTest). Les plug-ins concrets (Yousign à distance = SIG07 ; Wacom sur place = SIG08) ne sont
/// PAS livrés ici — SIG03 livre l'abstraction + un fournisseur Fake de parité (tests).
/// </summary>
public interface ISignatureProvider
{
    /// <summary>Capacités déclarées du fournisseur — la seule source de vérité de son comportement.</summary>
    SignatureProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Demande une signature. Si le niveau ou la localisation demandés ne sont pas dans les
    /// <see cref="Capabilities"/>, retourne un résultat <c>CapabilityNotSupported</c> (jamais d'exception).
    /// </summary>
    /// <param name="request">Demande de signature (tenant, document, niveau et localisation demandés).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureRequestResult> RequestSignatureAsync(
        SignatureRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Relit l'état d'une demande déjà soumise (réconciliation quand le flag
    /// <see cref="CompletionTransport.Polling"/> est déclaré, ou relecture après un webhook).
    /// </summary>
    /// <param name="providerReference">Référence de la demande côté fournisseur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureStatus> GetSignatureStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Télécharge la preuve de signature (rapatriement WORM via <c>Archive.Contracts</c> au niveau
    /// appelant, jamais par le plug-in — SIG07). Capacité absente → résultat typé, jamais d'exception.
    /// </summary>
    /// <param name="providerReference">Référence de la demande côté fournisseur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureProof> DownloadProofAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Traite un webhook de complétion. Pertinent UNIQUEMENT si le flag
    /// <see cref="CompletionTransport.Webhook"/> est déclaré : un fournisseur sans ce flag (ex. capteur sur
    /// place) retourne un résultat <c>CapabilityNotSupported</c> (INV-SIGPROV-3), jamais une exception.
    /// </summary>
    /// <param name="context">Contexte du webhook (corps brut, en-têtes, handle de tenant).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureWebhookResult> HandleWebhookAsync(
        SignatureWebhookContext context,
        CancellationToken cancellationToken = default);
}
