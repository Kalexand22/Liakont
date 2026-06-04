namespace Liakont.Modules.Documents.Contracts.Deduplication;

using System;

/// <summary>
/// Résultat d'anti-doublon (item TRK03, F06 §4) : le verdict et, le cas échéant, l'identifiant du
/// document antérieur concerné — le document <c>Issued</c> qui bloque (4.2/4.4) ou le document
/// <c>RejectedByPa</c> à superséder (4.3). <c>null</c> pour <see cref="DuplicateCheckDecision.Send"/>.
/// </summary>
public sealed record DuplicateCheckResult
{
    /// <summary>Verdict d'anti-doublon (F06 §4).</summary>
    public required DuplicateCheckDecision Decision { get; init; }

    /// <summary>
    /// Document antérieur concerné : l'émis bloquant (4.2/4.4) ou le rejeté à superséder (4.3) ;
    /// <c>null</c> pour un envoi inédit (4.5).
    /// </summary>
    public Guid? RelatedDocumentId { get; init; }

    /// <summary>Vrai si l'envoi est autorisé (document inédit OU renvoi après rejet — F06 §4.5 / §4.3).</summary>
    public bool MaySend => Decision is DuplicateCheckDecision.Send or DuplicateCheckDecision.ResendSupersedingRejected;
}
