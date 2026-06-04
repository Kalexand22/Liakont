namespace Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// Entrée du moteur de rapprochement (item TRK07) : un PDF du pool non lié, avec son nom de fichier et
/// le texte qui en a été extrait (PDF natif sans OCR en V1 — <c>null</c> si l'extraction n'a rien donné,
/// ex. PDF scanné). Le moteur est PUR : il ne lit jamais le fichier lui-même, il reçoit le texte déjà
/// extrait (frontière testable, l'extraction vit dans l'Infrastructure).
/// </summary>
/// <param name="PoolPdfId">Identifiant stable du dépôt dans le pool (clé de file d'attente).</param>
/// <param name="FileName">Nom de fichier lisible du PDF (stratégie 1).</param>
/// <param name="ExtractedText">Texte extrait du PDF, ou <c>null</c> si aucun texte exploitable (stratégie 2).</param>
public sealed record PooledPdfContent(string PoolPdfId, string FileName, string? ExtractedText);
