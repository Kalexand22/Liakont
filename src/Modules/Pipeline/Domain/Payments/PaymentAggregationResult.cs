namespace Liakont.Modules.Pipeline.Domain.Payments;

using System.Collections.Generic;

/// <summary>Résultat de l'agrégation de paiement (PIP03a) : les agrégats jour×taux + les encaissements écartés.</summary>
public sealed record PaymentAggregationResult
{
    /// <summary>Agrégats jour×taux calculés (qualifiés : transmissibles / suspendus / non requis / capacité en attente).</summary>
    public required IReadOnlyList<PaymentDailyAggregate> Aggregates { get; init; }

    /// <summary>Encaissements écartés (Mixte suspendu, livraison de biens non requis, taux non résolu, total nul…).</summary>
    public required IReadOnlyList<PaymentExclusion> Exclusions { get; init; }
}
