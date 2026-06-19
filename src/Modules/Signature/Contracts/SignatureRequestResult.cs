namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Résultat d'une demande de signature (ADR-0027 §3). Calqué sur <c>PaSendResult</c> : une capacité
/// absente porte l'état <see cref="SignatureRequestState.CapabilityNotSupported"/> et le détail
/// journalisable dans <see cref="CapabilityNotSupported"/> — jamais une exception, jamais un blocage
/// (INV-SIGPROV-5). La réponse brute du fournisseur est conservée pour la piste d'audit.
/// </summary>
public sealed record SignatureRequestResult
{
    /// <summary>État de la demande (soumise, complétée, rejetée, capacité absente, erreur technique).</summary>
    public required SignatureRequestState State { get; init; }

    /// <summary>Référence attribuée par le fournisseur (pour relire le statut / la preuve), ou <c>null</c>.</summary>
    public string? ProviderReference { get; init; }

    /// <summary>
    /// Détail de la capacité absente quand <see cref="State"/> vaut
    /// <see cref="SignatureRequestState.CapabilityNotSupported"/> ; <c>null</c> sinon.
    /// </summary>
    public SignatureCapabilityNotSupportedResult? CapabilityNotSupported { get; init; }

    /// <summary>Réponse brute du fournisseur, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }

    /// <summary>Construit un résultat « demande soumise » (complétion asynchrone à venir).</summary>
    /// <param name="providerReference">Référence attribuée par le fournisseur.</param>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static SignatureRequestResult Submitted(string providerReference, string? rawResponse = null) => new()
    {
        State = SignatureRequestState.Submitted,
        ProviderReference = providerReference,
        RawResponse = rawResponse,
    };

    /// <summary>Construit un résultat « demande complétée » de façon synchrone (capteur sur place).</summary>
    /// <param name="providerReference">Référence attribuée par le fournisseur.</param>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static SignatureRequestResult Completed(string providerReference, string? rawResponse = null) => new()
    {
        State = SignatureRequestState.Completed,
        ProviderReference = providerReference,
        RawResponse = rawResponse,
    };

    /// <summary>Construit un résultat « rejeté par le fournisseur » (pas de retry).</summary>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static SignatureRequestResult Rejected(string? rawResponse = null) => new()
    {
        State = SignatureRequestState.Rejected,
        RawResponse = rawResponse,
    };

    /// <summary>Construit un résultat « erreur technique » re-tentable.</summary>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static SignatureRequestResult Technical(string? rawResponse = null) => new()
    {
        State = SignatureRequestState.TechnicalError,
        RawResponse = rawResponse,
    };

    /// <summary>Construit un résultat « capacité absente » (jamais d'exception, jamais de blocage).</summary>
    /// <param name="capabilityGap">Détail journalisable de la capacité manquante.</param>
    public static SignatureRequestResult NotSupported(SignatureCapabilityNotSupportedResult capabilityGap) => new()
    {
        State = SignatureRequestState.CapabilityNotSupported,
        CapabilityNotSupported = capabilityGap,
    };
}
