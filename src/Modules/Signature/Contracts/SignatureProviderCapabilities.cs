namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Capacités déclarées d'un fournisseur de signature — la SEULE source de vérité de son comportement
/// (ADR-0027 §2 ; F17 §2). Calqué EXACTEMENT sur <c>PaCapabilities</c> du module Transmission : le produit
/// s'adapte aux capacités déclarées, il ne fait jamais <c>if (provider is Yousign)</c> ni de flag produit
/// (CLAUDE.md n°6/8/16 ; INV-SIGPROV-1). Les axes <see cref="Mode"/> (localisation),
/// <see cref="CompletionTransport"/> (transport de complétion) et <see cref="SupportedLevels"/> (ensemble
/// de niveaux activés) sont modélisés EXPLICITEMENT et ORTHOGONALEMENT — jamais l'un déduit de l'autre.
/// </summary>
public sealed record SignatureProviderCapabilities
{
    /// <summary>Nom du fournisseur (ex. « Yousign ») — porté dans les messages opérateur (français).</summary>
    public required string ProviderName { get; init; }

    /// <summary>Localisation(s) de signature offerte(s) — <see cref="SignatureMode"/> en <c>[Flags]</c>.</summary>
    public SignatureMode Mode { get; init; } = SignatureMode.None;

    /// <summary>Transport(s) de complétion offert(s) — <see cref="CompletionTransport"/> en <c>[Flags]</c>.</summary>
    public CompletionTransport CompletionTransport { get; init; } = CompletionTransport.None;

    /// <summary>
    /// ENSEMBLE des niveaux de preuve RÉELLEMENT activés (jamais un maximum ordonné — INV-SIGPROV-4).
    /// <see cref="SignatureLevel.Recorded"/> est toujours implicitement disponible (voir <see cref="Supports(SignatureLevel)"/>).
    /// </summary>
    public SignatureLevel SupportedLevels { get; init; } = SignatureLevel.None;

    /// <summary>Pré-vérification d'identité du signataire — capacité TECHNIQUE, jamais un gate imposé (F17 §7).</summary>
    public bool SupportsSignerIdentityVerification { get; init; }

    /// <summary>Scellement par liaison de hash du document (eIDAS art. 26 d).</summary>
    public bool SupportsDocumentHashBinding { get; init; }

    /// <summary>Capture biométrique brute (la capture seule n'est pas gouvernée par le flag de matching ci-dessous).</summary>
    public bool SupportsBiometricCapture { get; init; }

    /// <summary>
    /// Comparaison de gabarits biométriques — OPT-IN, <c>false</c> par défaut (bascule RGPD art. 9 ;
    /// la qualification fine relève du DPO du client, tranchée dans ADR-0030 — INV-SIGPROV-7).
    /// </summary>
    public bool SupportsBiometricTemplateMatching { get; init; }

    /// <summary>Téléchargement de la preuve de signature (dossier de preuve / PDF signé) — pour le rapatriement WORM.</summary>
    public bool SupportsProofDownload { get; init; }

    /// <summary>Taille maximale d'un document en octets, ou <c>null</c> si le fournisseur ne déclare pas de limite.</summary>
    public long? MaxDocumentSizeBytes { get; init; }

    /// <summary>
    /// Vrai si le niveau est dans l'ENSEMBLE déclaré (test d'APPARTENANCE, jamais une comparaison ordonnée —
    /// INV-SIGPROV-4). <see cref="SignatureLevel.Recorded"/> est toujours disponible (acceptation enregistrée
    /// sans signature, défaut conforme ADR-0024). Centralise le test pour que ni le produit ni un plug-in
    /// n'aient à <c>if</c> sur le type de fournisseur (CLAUDE.md n°16).
    /// </summary>
    /// <param name="level">Niveau de preuve à tester (une seule valeur de drapeau).</param>
    public bool Supports(SignatureLevel level) =>
        level == SignatureLevel.Recorded || (level != SignatureLevel.None && SupportedLevels.HasFlag(level));

    /// <summary>Vrai si la localisation est déclarée (test d'appartenance au <c>[Flags]</c> <see cref="Mode"/>).</summary>
    /// <param name="mode">Localisation à tester (une seule valeur de drapeau).</param>
    public bool Supports(SignatureMode mode) =>
        mode != SignatureMode.None && Mode.HasFlag(mode);
}
