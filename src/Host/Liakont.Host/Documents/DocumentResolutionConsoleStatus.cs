namespace Liakont.Host.Documents;

/// <summary>
/// Issue d'une action de RÉSOLUTION TERMINALE déclenchée depuis la console (WEB03c) — traitement manuel
/// hors passerelle ou liaison à un document de remplacement. Reflète, sans exception, le résultat du port
/// <c>IDocumentLifecycle</c> (parité avec les endpoints API02c <c>resolve-manually</c> / <c>supersede</c>),
/// plus les validations d'entrée que la console fait AVANT d'appeler le port (motif/remplaçant manquant) —
/// pour qu'un refus attendu reste un message opérateur en français (CLAUDE.md n°12), jamais une erreur 500.
/// La traduction en message d'affichage reste à la présentation (le composant Blazor).
/// </summary>
internal enum DocumentResolutionConsoleStatus
{
    /// <summary>La résolution a été appliquée (état + événement d'audit append-only écrits par le port).</summary>
    Succeeded,

    /// <summary>Motif de traitement manuel absent : il est OBLIGATOIRE (journalisé dans la piste d'audit, F06 §3).</summary>
    ReasonRequired,

    /// <summary>Aucun document de remplacement choisi : l'identifiant du remplaçant est obligatoire (F06 §4).</summary>
    ReplacementRequired,

    /// <summary>Le remplaçant désigné est le document lui-même : un document ne peut pas se remplacer lui-même.</summary>
    ReplacementIsSelf,

    /// <summary>Document introuvable dans ce tenant (lecture tenant-scopée — il n'existe pas, ou n'appartient pas au tenant).</summary>
    DocumentNotFound,

    /// <summary>L'état courant du document n'autorise pas cette résolution (machine à états du module Documents).</summary>
    InvalidState,

    /// <summary>Le document de remplacement est introuvable dans ce tenant (impossible de lier à un remplaçant inexistant).</summary>
    ReplacementNotFound,
}
