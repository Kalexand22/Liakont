namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Résultat TYPÉ d'un appel dont la capacité ou le niveau n'est pas pris en charge par le fournisseur
/// (ADR-0027 §3 ; INV-SIGPROV-5). Calqué sur <c>PaCapabilityNotSupportedResult</c> : porte de quoi
/// JOURNALISER un message opérateur en français (CLAUDE.md n°12), TYPÉ et exploitable sans parser de
/// texte. JAMAIS une exception, JAMAIS un blocage du produit (la signature est optionnelle).
/// </summary>
public sealed record SignatureCapabilityNotSupportedResult
{
    /// <summary>Nom du fournisseur concerné (ex. « Yousign »).</summary>
    public required string ProviderName { get; init; }

    /// <summary>Capacité absente, typée (journalisable et exploitable sans parser de texte).</summary>
    public required SignatureCapability Capability { get; init; }

    /// <summary>Message opérateur prêt à journaliser, en français.</summary>
    public required string OperatorMessage { get; init; }

    /// <summary>Construit un résultat « capacité absente » avec le message opérateur français standard.</summary>
    /// <param name="providerName">Nom du fournisseur concerné.</param>
    /// <param name="capability">Capacité non prise en charge.</param>
    public static SignatureCapabilityNotSupportedResult Create(string providerName, SignatureCapability capability)
    {
        var libelle = FrenchLabel(capability);
        return new SignatureCapabilityNotSupportedResult
        {
            ProviderName = providerName,
            Capability = capability,
            OperatorMessage =
                $"En attente : le fournisseur de signature « {providerName} » ne prend pas en charge {libelle}.",
        };
    }

    /// <summary>Construit un résultat « niveau non activé » à partir d'un <see cref="SignatureLevel"/>.</summary>
    /// <param name="providerName">Nom du fournisseur concerné.</param>
    /// <param name="level">Niveau de preuve demandé mais absent de l'ensemble déclaré.</param>
    public static SignatureCapabilityNotSupportedResult Create(string providerName, SignatureLevel level) =>
        Create(providerName, ToCapability(level));

    /// <summary>Construit un résultat « localisation non offerte » à partir d'un <see cref="SignatureMode"/>.</summary>
    /// <param name="providerName">Nom du fournisseur concerné.</param>
    /// <param name="mode">Localisation demandée mais non offerte.</param>
    public static SignatureCapabilityNotSupportedResult Create(string providerName, SignatureMode mode) =>
        Create(providerName, ToCapability(mode));

    private static SignatureCapability ToCapability(SignatureLevel level) => level switch
    {
        SignatureLevel.Recorded => SignatureCapability.RecordedLevel,
        SignatureLevel.SES => SignatureCapability.SimpleLevel,
        SignatureLevel.AES => SignatureCapability.AdvancedLevel,
        SignatureLevel.QES => SignatureCapability.QualifiedLevel,
        _ => SignatureCapability.RecordedLevel,
    };

    private static SignatureCapability ToCapability(SignatureMode mode) => mode switch
    {
        SignatureMode.Remote => SignatureCapability.RemoteSignature,
        SignatureMode.OnSite => SignatureCapability.OnSiteSignature,
        _ => SignatureCapability.RemoteSignature,
    };

    private static string FrenchLabel(SignatureCapability capability) => capability switch
    {
        SignatureCapability.RemoteSignature => "la signature à distance",
        SignatureCapability.OnSiteSignature => "la signature sur place",
        SignatureCapability.WebhookCompletion => "la complétion par webhook",
        SignatureCapability.RecordedLevel => "l'acceptation enregistrée",
        SignatureCapability.SimpleLevel => "la signature simple (SES)",
        SignatureCapability.AdvancedLevel => "la signature avancée (AES)",
        SignatureCapability.QualifiedLevel => "la signature qualifiée (QES)",
        SignatureCapability.SignerIdentityVerification => "la vérification d'identité du signataire",
        SignatureCapability.DocumentHashBinding => "le scellement par liaison de hash",
        SignatureCapability.BiometricCapture => "la capture biométrique",
        SignatureCapability.ProofDownload => "le téléchargement de la preuve de signature",
        _ => capability.ToString(),
    };
}
