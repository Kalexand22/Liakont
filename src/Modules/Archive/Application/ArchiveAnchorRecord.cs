namespace Liakont.Modules.Archive.Application;

using System;
using Liakont.Modules.Archive.Domain;

/// <summary>Une ligne d'ancrage temporel scellée (<c>documents.archive_anchors</c>, append-only/WORM, TRK06).</summary>
/// <param name="AnchorId">Identifiant de l'ancrage.</param>
/// <param name="ChainHeadEntryId">Entrée de coffre dont le <c>chain_hash</c> est ancré (FK <c>archive_entries</c>).</param>
/// <param name="ChainHeadHash">Empreinte de tête de chaîne ancrée.</param>
/// <param name="Method">Méthode d'ancrage.</param>
/// <param name="Status">État de l'ancrage (<c>anchored</c> ; <c>pending</c> réservé au cycle OpenTimestamps V1.1).</param>
/// <param name="ProofPath">Chemin (relatif au tenant) de la preuve dans le coffre, ou <c>null</c>.</param>
/// <param name="AnchoredUtc">Instant attesté par le service d'horodatage (UTC), ou <c>null</c>.</param>
/// <param name="RequestedUtc">Horodatage de la demande d'ancrage (UTC).</param>
public sealed record ArchiveAnchorRecord(
    Guid AnchorId,
    Guid ChainHeadEntryId,
    string ChainHeadHash,
    TimestampAnchorMethod Method,
    string Status,
    string? ProofPath,
    DateTimeOffset? AnchoredUtc,
    DateTimeOffset RequestedUtc);
