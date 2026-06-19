namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Capacité de signature absente, TYPÉE (modèle <c>PaCapability</c> du module Transmission). Sert à
/// journaliser un résultat NotSupported sans parser de texte (porté par
/// <see cref="SignatureCapabilityNotSupportedResult"/>). Couvre les trois axes de
/// <see cref="SignatureProviderCapabilities"/> : la localisation (<see cref="SignatureMode"/>), le
/// transport de complétion (<see cref="CompletionTransport"/>), le niveau de preuve
/// (<see cref="SignatureLevel"/>) et les capacités techniques booléennes.
/// </summary>
public enum SignatureCapability
{
    /// <summary>Signature à distance (<see cref="SignatureMode.Remote"/>).</summary>
    RemoteSignature,

    /// <summary>Signature sur place (<see cref="SignatureMode.OnSite"/>).</summary>
    OnSiteSignature,

    /// <summary>Complétion par webhook (<see cref="CompletionTransport.Webhook"/>).</summary>
    WebhookCompletion,

    /// <summary>Niveau enregistré (<see cref="SignatureLevel.Recorded"/>).</summary>
    RecordedLevel,

    /// <summary>Niveau SES (<see cref="SignatureLevel.SES"/>).</summary>
    SimpleLevel,

    /// <summary>Niveau AES (<see cref="SignatureLevel.AES"/>).</summary>
    AdvancedLevel,

    /// <summary>Niveau QES (<see cref="SignatureLevel.QES"/>).</summary>
    QualifiedLevel,

    /// <summary>Pré-vérification d'identité du signataire.</summary>
    SignerIdentityVerification,

    /// <summary>Scellement par liaison de hash du document (eIDAS art. 26 d).</summary>
    DocumentHashBinding,

    /// <summary>Capture biométrique brute.</summary>
    BiometricCapture,

    /// <summary>Téléchargement de la preuve de signature.</summary>
    ProofDownload,
}
