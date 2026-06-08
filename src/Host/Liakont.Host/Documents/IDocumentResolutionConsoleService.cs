namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Actions de RÉSOLUTION TERMINALE d'un document depuis la console (WEB03c) : traitement manuel hors
/// passerelle (motif obligatoire) et liaison à un document de remplacement, plus la recherche des
/// candidats au remplacement. Isole l'action hors de la page Blazor (la page reste présentationnelle —
/// CLAUDE.md n°19) et la rend testable. Réutilise le PORT du module Documents (<c>IDocumentLifecycle</c>),
/// exactement comme les endpoints API02c : aucune logique fiscale ni machine à états ici (transition + audit
/// append-only = du ressort du domaine). L'identité enregistrée est celle de l'opérateur AUTHENTIFIÉ, jamais
/// une valeur fournie par l'UI (parité avec l'endpoint, CLAUDE.md n°12). Tenant-scopé par construction
/// (la connexion EST le tenant — CLAUDE.md n°9).
/// </summary>
internal interface IDocumentResolutionConsoleService
{
    /// <summary>
    /// Marque le document <paramref name="documentId"/> « traité manuellement hors passerelle »
    /// (Blocked ou RejectedByPa → ManuallyHandled). Le <paramref name="reason"/> est OBLIGATOIRE
    /// (validé avant l'appel du port — sinon <see cref="DocumentResolutionConsoleStatus.ReasonRequired"/>) :
    /// il est inscrit dans la piste d'audit append-only (F06 §3).
    /// </summary>
    Task<DocumentResolutionConsoleStatus> ResolveManuallyAsync(
        Guid documentId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lie le document rejeté <paramref name="documentId"/> à son remplaçant
    /// (<paramref name="replacementDocumentId"/>, déjà reçu via l'agent — F06 §4) : RejectedByPa → Superseded.
    /// Refuse un remplaçant vide (<see cref="DocumentResolutionConsoleStatus.ReplacementRequired"/>) ou
    /// identique au document (<see cref="DocumentResolutionConsoleStatus.ReplacementIsSelf"/>).
    /// </summary>
    Task<DocumentResolutionConsoleStatus> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche les documents candidats au remplacement dans le tenant courant (filtre texte libre :
    /// numéro, référence source, acheteur), en EXCLUANT le document rejeté lui-même
    /// (<paramref name="rejectedDocumentId"/>). Liste bornée (premiers résultats), triée par dernière mise à
    /// jour décroissante. Aucune règle métier : projection de la lecture du module.
    /// </summary>
    Task<IReadOnlyList<DocumentReplacementCandidate>> SearchReplacementCandidatesAsync(
        Guid rejectedDocumentId, string? search, CancellationToken cancellationToken = default);
}
