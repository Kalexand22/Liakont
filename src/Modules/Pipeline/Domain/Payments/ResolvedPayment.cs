namespace Liakont.Modules.Pipeline.Domain.Payments;

using System;

/// <summary>
/// Encaissement déjà RÉSOLU (rattaché à un document dont la ventilation a été chargée depuis le snapshot)
/// prêt à être ventilé par l'agrégateur. Les encaissements non rattachés / sans snapshot sont écartés AVANT
/// (côté job, I/O) et ne parviennent jamais ici : le calculateur ne reçoit que des encaissements résolus.
/// </summary>
public sealed record ResolvedPayment
{
    /// <summary>Date d'encaissement (exigibilité à l'encaissement — l'agrégat est rattaché à ce jour).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Montant encaissé (decimal, peut être négatif : trop-perçu / remboursement — F09 §5.4).</summary>
    public required decimal Amount { get; init; }

    /// <summary>Numéro du bordereau rattaché (pour l'identification d'une exclusion éventuelle).</summary>
    public string? RelatedDocumentNumber { get; init; }

    /// <summary>Ventilation par taux du document rattaché.</summary>
    public required DocumentVentilation Document { get; init; }
}
