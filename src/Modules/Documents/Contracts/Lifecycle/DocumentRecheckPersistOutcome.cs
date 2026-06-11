namespace Liakont.Modules.Documents.Contracts.Lifecycle;

/// <summary>
/// Résultat de la persistance d'une RE-VÉRIFICATION opérateur (item FIX02, <see cref="IDocumentLifecycle"/>).
/// Comme une résolution terminale (<see cref="DocumentResolutionOutcome"/>), une re-vérification est une action
/// d'opérateur dont le refus est ATTENDU : un geste concurrent (résolution manuelle, ou un recheck qui débloque)
/// peut sortir le document de <c>Blocked</c> entre la lecture NON verrouillée du recheck et l'écriture VERROUILLÉE
/// (<c>FOR UPDATE</c>) du cycle de vie. Ce refus est retourné comme un résultat — jamais une exception — pour un
/// mapping HTTP propre (409/404, jamais 500) et pour ne JAMAIS inscrire un fait d'audit sur un document qui n'est
/// plus dans l'état attendu. La décision est AUTORITAIRE dans la transaction d'écriture (sous verrou), donc sans
/// fenêtre TOCTOU. (Une vraie erreur de persistance reste une exception qui remonte — elle n'est pas masquée.)
/// </summary>
public enum DocumentRecheckPersistOutcome
{
    /// <summary>Le fait d'audit de re-vérification (toujours bloqué) ou la transition de déblocage a été persisté(e).</summary>
    Persisted,

    /// <summary>Le document ciblé n'existe plus dans le tenant courant (→ 404).</summary>
    DocumentNotFound,

    /// <summary>Le document n'est plus <c>Blocked</c> (geste opérateur concurrent) : la re-vérification ne s'applique plus (→ 409).</summary>
    StateChanged,
}
