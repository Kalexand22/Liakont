namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat individuel d'un document dans un lot poussé par l'agent (F12 §3.4). Le lot n'est jamais
/// rejeté en bloc pour un seul document invalide : chaque document a son propre statut.
/// </summary>
public enum DocumentPushStatus
{
    /// <summary>Document accepté (créé en état Detected sur la plateforme).</summary>
    Accepted = 1,

    /// <summary>Doublon : ce payload est déjà connu pour ce tenant — aucun effet (anti-doublon F6).</summary>
    Duplicate = 2,

    /// <summary>Document rejeté (payload non conforme au contrat) — voir le motif.</summary>
    Rejected = 3,
}
