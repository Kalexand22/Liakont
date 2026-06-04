namespace Liakont.Modules.Archive.Application;

/// <summary>Une pièce attendue mais ABSENTE, avec son motif (jamais une absence silencieuse — TRK05 §2).</summary>
/// <param name="Piece">Identifiant stable de la pièce (« facture-pa », « bordereau-source »).</param>
/// <param name="Reason">Motif d'absence, en français.</param>
public sealed record ArchiveAbsentPiece(string Piece, string Reason);
