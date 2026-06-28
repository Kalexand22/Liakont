namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Re-vérification À LA DEMANDE d'UN document bloqué OU rejeté par la Plateforme Agréée (item API02b, endpoint
/// <c>POST /documents/{id}/recheck</c>) : re-joue le CHECK COMPLET (mapping TVA → garde-fou production →
/// validation) sur le pivot stagé du document, SANS attendre le prochain traitement, et le fait passer
/// <c>ReadyToSend</c> s'il passe désormais (table TVA complétée/validée, mentions B2B saisies, verdict garde-fou
/// B2C posé). Un document <c>RejectedByPa</c> dont la cause n'est PAS corrigée est TRANSITIONNÉ vers
/// <c>Blocked</c> (il quitte le cul-de-sac pour montrer le motif à corriger — « bloquer plutôt qu'envoyer faux »,
/// CLAUDE.md n°3). C'est la SEULE surface (Contracts) par laquelle la console déclenche une re-vérification ;
/// l'implémentation réutilise la SOURCE UNIQUE de la décision de blocage fiscal (jamais une seconde
/// implémentation divergente — CLAUDE.md n°2/3). Tenant-scopée (le tenant est résolu par la requête).
/// </summary>
public interface IDocumentRecheckService
{
    /// <summary>
    /// Re-vérifie le document <paramref name="documentId"/> du tenant courant à la demande de l'opérateur
    /// <paramref name="operatorIdentity"/> (GUID) — <paramref name="operatorName"/> est son nom d'affichage
    /// capturé pour la piste d'audit (item FIX305 ; peut être <c>null</c>, repli sur le GUID). Retourne l'issue
    /// (introuvable, ni bloqué ni rejeté, contenu indisponible, passé en ReadyToSend, ou toujours bloqué avec les
    /// nouveaux motifs). Selon l'état d'entrée, transitionne vers <c>ReadyToSend</c> (cause corrigée) ou — pour un
    /// document rejeté par la PA dont la cause n'est pas corrigée — vers <c>Blocked</c> (le motif réévalué devient
    /// alors le motif courant à corriger) ; un document déjà <c>Blocked</c> qui reste « pas prêt » ne transitionne
    /// PAS (la machine à états interdit <c>Blocked → Blocked</c>). Dans TOUS les cas où le recheck a réellement
    /// tourné (FIX02), un fait d'audit append-only portant l'opérateur et le résultat est inscrit : passage
    /// <c>ReadyToSend</c> attribué, ou toujours bloqué (événement <c>RecheckedStillBlocked</c> pour un Blocked,
    /// <c>DocumentBlocked</c> pour la transition d'un RejectedByPa — portant le motif réévalué qui devient le motif
    /// courant affiché).
    /// </summary>
    Task<DocumentRecheckResult> RecheckAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-vérifie EN MASSE les documents <paramref name="documentIds"/> du tenant courant à la demande de
    /// l'opérateur <paramref name="operatorIdentity"/> (GUID ; <paramref name="operatorName"/> = nom d'affichage
    /// capturé pour l'audit, FIX305) (FIX207 — actions « Revérifier la sélection » / « Revérifier
    /// tout »). Boucle <see cref="RecheckAsync"/> sur les identifiants DISTINCTS (un même document n'est re-vérifié
    /// et audité qu'une fois) et agrège l'issue dans un <see cref="DocumentBulkRecheckSummary"/>. Chaque document
    /// effectivement re-vérifié laisse SA trace d'audit append-only attribuée à l'opérateur (FIX02), exactement
    /// comme la re-vérification unitaire — aucune logique fiscale n'est dupliquée. Tenant-scopée (le tenant est
    /// résolu par la requête) ; honore <paramref name="cancellationToken"/> entre documents.
    /// </summary>
    Task<DocumentBulkRecheckSummary> RecheckManyAsync(
        IReadOnlyList<Guid> documentIds, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);
}
