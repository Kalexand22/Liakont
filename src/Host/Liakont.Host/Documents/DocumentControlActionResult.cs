namespace Liakont.Host.Documents;

/// <summary>
/// Résultat d'une action de l'onglet Contrôles (WEB03b), présenté tel quel à l'opérateur. <see cref="Success"/>
/// distingue l'aboutissement de l'action d'un refus métier (état incompatible, document introuvable, contenu
/// indisponible) ; <see cref="Message"/> est le message opérateur en français (numéro de document inclus
/// quand c'est pertinent, CLAUDE.md n°12) ; <see cref="NewState"/> est l'état résultant du document, ou
/// <c>null</c> sur un refus. La page recharge ensuite le détail pour refléter l'historique et les contrôles.
/// </summary>
internal sealed record DocumentControlActionResult(bool Success, string Message, string? NewState)
{
    /// <summary>Action aboutie : message opérateur + état résultant du document.</summary>
    public static DocumentControlActionResult Ok(string message, string? newState) => new(true, message, newState);

    /// <summary>Refus métier (état incompatible, introuvable, contenu indisponible) : message opérateur, aucun changement d'état.</summary>
    public static DocumentControlActionResult Failure(string message) => new(false, message, NewState: null);
}
