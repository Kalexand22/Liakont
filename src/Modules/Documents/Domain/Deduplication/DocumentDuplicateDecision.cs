namespace Liakont.Modules.Documents.Domain.Deduplication;

/// <summary>
/// Résultat de l'anti-doublon (item TRK03, F06 §4) : le verdict et, le cas échéant, l'identifiant du
/// document antérieur concerné — le document <c>Issued</c> qui bloque (4.2/4.4) ou le document
/// <c>RejectedByPa</c> à superséder (4.3). <c>null</c> pour <see cref="DocumentDuplicateOutcome.Send"/>.
/// </summary>
public readonly record struct DocumentDuplicateDecision(DocumentDuplicateOutcome Outcome, Guid? RelatedDocumentId)
{
    /// <summary>Vrai si l'envoi est autorisé (document inédit OU renvoi après rejet — F06 §4.5 / §4.3).</summary>
    public bool MaySend => Outcome is DocumentDuplicateOutcome.Send or DocumentDuplicateOutcome.ResendSupersedingRejected;
}
