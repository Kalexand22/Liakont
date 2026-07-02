namespace Liakont.Host.Ged;

/// <summary>Détail du verdict d'intégrité pour la fiche document GED (GED09b).</summary>
/// <param name="State">Verdict d'intégrité.</param>
/// <param name="IndexedHash">Empreinte indexée (<c>content_hash</c>), ou <c>null</c>.</param>
/// <param name="RecomputedHash">Empreinte recalculée depuis les octets du coffre, ou <c>null</c>.</param>
/// <param name="Detail">Message opérateur français précisant une divergence, ou <c>null</c>.</param>
public sealed record GedDocumentIntegrityView(
    GedDocumentIntegrityState State,
    string? IndexedHash,
    string? RecomputedHash,
    string? Detail);
