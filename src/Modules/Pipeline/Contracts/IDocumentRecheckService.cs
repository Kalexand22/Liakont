namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Re-vérification À LA DEMANDE d'UN document bloqué (item API02b, endpoint <c>POST /documents/{id}/recheck</c>) :
/// re-joue le CHECK COMPLET (mapping TVA → garde-fou production → validation) sur le pivot stagé du document,
/// SANS attendre le prochain traitement, et le fait passer <c>Blocked → ReadyToSend</c> s'il passe désormais
/// (table TVA complétée/validée, verdict garde-fou B2C posé). C'est la SEULE surface (Contracts) par laquelle
/// la console déclenche une re-vérification ; l'implémentation réutilise la SOURCE UNIQUE de la décision de
/// blocage fiscal (jamais une seconde implémentation divergente — CLAUDE.md n°2/3). Tenant-scopée (le tenant
/// est résolu par la requête).
/// </summary>
public interface IDocumentRecheckService
{
    /// <summary>
    /// Re-vérifie le document <paramref name="documentId"/> du tenant courant à la demande de l'opérateur
    /// <paramref name="operatorIdentity"/>. Retourne l'issue (introuvable, non bloqué, contenu indisponible,
    /// débloqué vers ReadyToSend, ou toujours bloqué avec les nouveaux motifs). Ne transitionne le document que
    /// vers <c>ReadyToSend</c> (la machine à états interdit <c>Blocked → Blocked</c>). Dans TOUS les cas où le
    /// recheck a réellement tourné (FIX02), un fait d'audit append-only portant l'opérateur et le résultat est
    /// inscrit : déblocage (événement <c>ReadyToSend</c> attribué) ou toujours bloqué (événement
    /// <c>RecheckedStillBlocked</c> portant le motif réévalué — qui devient le motif courant affiché).
    /// </summary>
    Task<DocumentRecheckResult> RecheckAsync(Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-vérifie EN MASSE les documents <paramref name="documentIds"/> du tenant courant à la demande de
    /// l'opérateur <paramref name="operatorIdentity"/> (FIX207 — actions « Revérifier la sélection » / « Revérifier
    /// tout »). Boucle <see cref="RecheckAsync"/> sur les identifiants DISTINCTS (un même document n'est re-vérifié
    /// et audité qu'une fois) et agrège l'issue dans un <see cref="DocumentBulkRecheckSummary"/>. Chaque document
    /// effectivement re-vérifié laisse SA trace d'audit append-only attribuée à l'opérateur (FIX02), exactement
    /// comme la re-vérification unitaire — aucune logique fiscale n'est dupliquée. Tenant-scopée (le tenant est
    /// résolu par la requête) ; honore <paramref name="cancellationToken"/> entre documents.
    /// </summary>
    Task<DocumentBulkRecheckSummary> RecheckManyAsync(
        IReadOnlyList<Guid> documentIds, string operatorIdentity, CancellationToken cancellationToken = default);
}
