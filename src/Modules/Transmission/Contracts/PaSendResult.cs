namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résultat d'un envoi de document ou d'e-reporting (F05 §3). Conserve la réponse brute
/// (<see cref="RawResponse"/>) pour la piste d'audit (F06/DR6) et les <see cref="Errors"/> intactes.
/// Une capacité absente n'est PAS une exception : l'état est
/// <see cref="PaSendState.CapabilityNotSupported"/> et <see cref="CapabilityNotSupported"/> porte le
/// détail journalisable (acceptance PAA01).
/// </summary>
public sealed record PaSendResult
{
    /// <summary>État de l'envoi (succès, rejet, erreur technique re-tentable, capacité absente…).</summary>
    public required PaSendState State { get; init; }

    /// <summary>Identifiant attribué par la PA, ou <c>null</c> si l'envoi n'a pas abouti.</summary>
    public string? PaDocumentId { get; init; }

    /// <summary>Identifiants des tax reports liés (F05 §3), jamais <c>null</c>.</summary>
    public IReadOnlyList<string> TaxReportIds { get; init; } = [];

    /// <summary>Erreurs remontées par la PA, intactes (F05 §3), jamais <c>null</c>.</summary>
    public IReadOnlyList<PaError> Errors { get; init; } = [];

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }

    /// <summary>
    /// Détail de la capacité absente quand <see cref="State"/> vaut
    /// <see cref="PaSendState.CapabilityNotSupported"/> ; <c>null</c> sinon.
    /// </summary>
    public PaCapabilityNotSupportedResult? CapabilityNotSupported { get; init; }

    /// <summary>Construit un résultat « émis / accepté » par la PA.</summary>
    /// <param name="paDocumentId">Identifiant attribué par la PA.</param>
    /// <param name="taxReportIds">Tax reports liés (facultatif).</param>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static PaSendResult Issued(
        string paDocumentId,
        IReadOnlyList<string>? taxReportIds = null,
        string? rawResponse = null) => new()
        {
            State = PaSendState.Issued,
            PaDocumentId = paDocumentId,
            TaxReportIds = taxReportIds ?? [],
            RawResponse = rawResponse,
        };

    /// <summary>Construit un résultat « rejeté par la PA » (4xx ou 200 + errors[]) — pas de retry.</summary>
    /// <param name="errors">Erreurs remontées par la PA.</param>
    /// <param name="paDocumentId">Identifiant côté PA s'il existe.</param>
    /// <param name="rawResponse">Réponse brute pour l'audit.</param>
    public static PaSendResult Rejected(
        IReadOnlyList<PaError> errors,
        string? paDocumentId = null,
        string? rawResponse = null) => new()
        {
            State = PaSendState.RejectedByPa,
            PaDocumentId = paDocumentId,
            Errors = errors,
            RawResponse = rawResponse,
        };

    /// <summary>Construit un résultat « erreur technique » re-tentable au prochain run.</summary>
    /// <param name="errors">Erreurs techniques (facultatif).</param>
    /// <param name="rawResponse">Réponse brute pour l'audit.</param>
    public static PaSendResult Technical(
        IReadOnlyList<PaError>? errors = null,
        string? rawResponse = null) => new()
        {
            State = PaSendState.TechnicalError,
            Errors = errors ?? [],
            RawResponse = rawResponse,
        };

    /// <summary>Construit un résultat « capacité absente » (jamais d'exception, jamais de blocage).</summary>
    /// <param name="capabilityGap">Détail journalisable de la capacité manquante.</param>
    public static PaSendResult NotSupported(PaCapabilityNotSupportedResult capabilityGap) => new()
    {
        State = PaSendState.CapabilityNotSupported,
        CapabilityNotSupported = capabilityGap,
    };
}
