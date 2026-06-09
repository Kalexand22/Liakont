namespace Liakont.Host.Reconciliation;

/// <summary>
/// Résultat d'une action opérateur de réconciliation (confirmer / rejeter / lier) déclenchée depuis la
/// console (WEB08). Modélise le succès ou l'échec avec un message FRANÇAIS prêt à l'affichage (CLAUDE.md
/// n°12) — la page n'a aucune logique de décision, elle relaie le message. Motif identique au résultat des
/// actions de document (<c>DocumentControlActionResult</c>) : aucune exception ne remonte à la page.
/// </summary>
internal sealed record ReconciliationActionResult(bool Succeeded, string Message)
{
    /// <summary>Action réussie, avec un message de confirmation pour l'opérateur.</summary>
    public static ReconciliationActionResult Ok(string message) => new(true, message);

    /// <summary>Action refusée ou en échec, avec un message d'explication (jamais avalé en silence).</summary>
    public static ReconciliationActionResult Failure(string message) => new(false, message);
}
