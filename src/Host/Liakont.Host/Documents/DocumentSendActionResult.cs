namespace Liakont.Host.Documents;

/// <summary>
/// Résultat d'une action d'envoi de la page Documents (WEB05 : « Envoyer la sélection », « Tout envoyer »,
/// « Lancer un traitement »), présenté tel quel à l'opérateur. <see cref="Success"/> distingue l'aboutissement
/// (au moins un déclenchement, ou un déclenchement groupé) d'un refus métier (permission manquante, tenant non
/// résolu, aucune cible) ; <see cref="Message"/> est le message opérateur en français, avec le numéro de
/// document quand c'est pertinent (CLAUDE.md n°12).
/// </summary>
internal sealed record DocumentSendActionResult(bool Success, string Message)
{
    /// <summary>Action aboutie : message opérateur récapitulant le déclenchement.</summary>
    public static DocumentSendActionResult Ok(string message) => new(true, message);

    /// <summary>Refus métier (permission manquante, tenant non résolu, aucun document prêt) : message opérateur.</summary>
    public static DocumentSendActionResult Failure(string message) => new(false, message);

    /// <summary>
    /// Concatène une information complémentaire au message en préservant le verdict <see cref="Success"/> (FIX202 :
    /// les documents ignorés à la sélection sont restitués À CÔTÉ du résultat du run, jamais à sa place). Renvoie
    /// l'instance inchangée si le suffixe est vide.
    /// </summary>
    public DocumentSendActionResult WithSuffix(string? suffix) =>
        string.IsNullOrWhiteSpace(suffix) ? this : this with { Message = Message + " " + suffix.Trim() };
}
