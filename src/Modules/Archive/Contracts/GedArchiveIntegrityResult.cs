namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// Résultat de la vérification d'intégrité d'un paquet GED (F19 §6.7). Porte l'empreinte INDEXÉE
/// (<c>content_hash</c>) et l'empreinte RECALCULÉE depuis les octets réels du coffre : leur égalité fonde
/// <see cref="GedArchiveIntegrityStatus.Verified"/>. <see cref="Detail"/> est un message opérateur FRANÇAIS
/// (jamais un dump technique) précisant la nature d'une divergence, ou <see langword="null"/> si intègre.
/// </summary>
/// <param name="Status">Verdict d'intégrité.</param>
/// <param name="IndexedContentHash">Empreinte SHA-256 (hex) telle qu'indexée dans <c>managed_documents.content_hash</c>, ou <see langword="null"/>.</param>
/// <param name="RecomputedContentHash">Empreinte SHA-256 (hex) recalculée depuis les octets re-lus du coffre, ou <see langword="null"/> si non re-lus.</param>
/// <param name="Detail">Message opérateur français précisant une divergence, ou <see langword="null"/>.</param>
public sealed record GedArchiveIntegrityResult(
    GedArchiveIntegrityStatus Status,
    string? IndexedContentHash,
    string? RecomputedContentHash,
    string? Detail);
