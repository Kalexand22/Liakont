namespace Liakont.SignatureProviders.Yousign;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Capacités DÉCLARÉES de Yousign — la SEULE source de vérité du comportement du produit (ADR-0027 §2 ;
/// ADR-0029 §1 ; CLAUDE.md n°6/8/16 ; INV-YOUSIGN-1). DÉFAUT DÉFENDABLE (F17 §10 #6) : on DÉCLARE au niveau
/// RÉELLEMENT vérifiable en SANDBOX ; les niveaux supérieurs (AES/QES) de l'offre souscrite = ACTIVATION au
/// déploiement, jamais supposés. Une capacité non vérifiée n'est PAS déclarée → un appel la demandant renvoie
/// un résultat TYPÉ <c>NotSupported</c> (jamais une exception, jamais un blocage produit). Yousign est un
/// fournisseur À DISTANCE (<see cref="SignatureMode.Remote"/>) à complétion par WEBHOOK (+ polling de
/// réconciliation), avec téléchargement de la preuve (rapatriement WORM par l'appelant).
/// </summary>
internal static class YousignCapabilities
{
    /// <summary>
    /// Capacités du build courant de SIG07. <see cref="SignatureLevel.SES"/> est le seul niveau DÉCLARÉ
    /// (vérifiable en sandbox sans pré-vérification d'identité) ; AES/QES restent NON déclarés
    /// (→ <c>NotSupported</c>) tant qu'ils ne sont pas activés/vérifiés au déploiement.
    /// </summary>
    public static SignatureProviderCapabilities Declared => new()
    {
        ProviderName = YousignDefaults.ProviderName,
        Mode = SignatureMode.Remote,

        // Webhook primaire + polling de réconciliation de secours (axes orthogonaux — ADR-0027 §2).
        CompletionTransport = CompletionTransport.Webhook | CompletionTransport.Polling,

        // Niveau réellement vérifié en sandbox uniquement ; AES/QES = activation au déploiement (jamais supposés).
        SupportedLevels = SignatureLevel.SES,

        // Capacité technique de pré-vérification d'identité offerte par l'API (jamais un gate imposé — F17 §7).
        SupportsSignerIdentityVerification = true,

        // Scellement par liaison de hash du document (eIDAS art. 26 d) — porté par DocumentHash de la demande.
        SupportsDocumentHashBinding = true,

        // Téléchargement du dossier de preuve / PDF signé → rapatriement WORM par l'appelant (ADR-0029 §5).
        SupportsProofDownload = true,

        // Fournisseur à distance : aucune capture biométrique (c'est le capteur sur place Wacom — SIG08).
        SupportsBiometricCapture = false,
        SupportsBiometricTemplateMatching = false,
        MaxDocumentSizeBytes = null,
    };
}
