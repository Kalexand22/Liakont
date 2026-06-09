namespace Liakont.Host.Documents;

/// <summary>
/// Récapitulatif des documents prêts à l'envoi du tenant courant, affiché AVANT le « Tout envoyer » pour la
/// confirmation (nombre + montant total TTC, F10). <see cref="TotalGross"/> est en <c>decimal</c> (CLAUDE.md n°1).
/// </summary>
internal sealed record DocumentSendSummary(int Count, decimal TotalGross);
