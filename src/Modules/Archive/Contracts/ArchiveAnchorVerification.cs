namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Vérification d'UNE preuve d'ancrage temporel du coffre (TRK06). Confirme que la preuve archivée
/// (jeton RFC 3161, fichier .ots) horodate bien une tête de chaîne réelle du tenant et que sa signature
/// est valide (et, si une TSA est épinglée, qu'elle émane bien de cette TSA).
/// </summary>
/// <param name="AnchorId">Identifiant de l'ancrage (<c>documents.archive_anchors</c>).</param>
/// <param name="Method">Méthode d'ancrage (<c>rfc3161</c>, <c>opentimestamps</c>, <c>none</c>).</param>
/// <param name="ChainHeadHash">Empreinte de tête de chaîne ancrée.</param>
/// <param name="ProofPath">Chemin (relatif au tenant) de la preuve dans le coffre, ou <c>null</c> si NoAnchor.</param>
/// <param name="Status">État de vérification : valide, invalide, ou non vérifiable par cette instance.</param>
/// <param name="AnchoredUtc">Instant attesté par la preuve (UTC), ou <c>null</c>.</param>
/// <param name="Detail">Message français (anomalie, ou caveat d'authentification TSA), ou <c>null</c>.</param>
public sealed record ArchiveAnchorVerification(
    Guid AnchorId,
    string Method,
    string ChainHeadHash,
    string? ProofPath,
    ArchiveAnchorVerificationStatus Status,
    DateTimeOffset? AnchoredUtc,
    string? Detail);
