namespace Liakont.Modules.Archive.Application;

/// <summary>Résultat détaillé d'un ancrage, pour journalisation et supervision (TRK06).</summary>
/// <param name="Status">L'issue de la tentative.</param>
/// <param name="Detail">Message français explicitant l'issue.</param>
/// <param name="Record">La ligne d'ancrage produite ou retrouvée, ou <c>null</c> si aucun ancrage.</param>
public sealed record AnchoringOutcome(AnchoringStatus Status, string Detail, ArchiveAnchorRecord? Record);
