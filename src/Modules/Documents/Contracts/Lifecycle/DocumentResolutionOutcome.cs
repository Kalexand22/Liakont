namespace Liakont.Modules.Documents.Contracts.Lifecycle;

/// <summary>
/// Résultat d'une RÉSOLUTION TERMINALE opérateur (API02c : « traité manuellement » / « remplacé »), demandée
/// depuis la console. Contrairement aux transitions du pipeline (<see cref="IDocumentLifecycle"/>, qui lèvent
/// sur transition illégale car c'est alors un bug interne), une action d'opérateur a des refus ATTENDUS
/// (document déjà résolu, remplaçant inexistant) : ils sont retournés comme un résultat — pas une exception —
/// pour un mapping HTTP propre (4xx, jamais 500). La décision reste AUTORITAIRE dans la transaction d'écriture
/// (sous verrou <c>FOR UPDATE</c>), donc sans fenêtre TOCTOU entre une lecture et l'écriture.
/// </summary>
public enum DocumentResolutionOutcome
{
    /// <summary>La transition terminale a été appliquée et persistée (état + événement d'audit append-only).</summary>
    Succeeded,

    /// <summary>Le document ciblé n'existe pas dans le tenant courant (→ 404).</summary>
    DocumentNotFound,

    /// <summary>L'état courant du document n'autorise pas cette résolution (machine à états TRK02 → 409).</summary>
    InvalidState,

    /// <summary>Remplacement : le document de remplacement n'existe pas dans le tenant courant (→ 409).</summary>
    ReplacementNotFound,
}
