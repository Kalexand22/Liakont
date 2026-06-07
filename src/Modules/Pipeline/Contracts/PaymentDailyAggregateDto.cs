namespace Liakont.Modules.Pipeline.Contracts;

using System;

/// <summary>
/// Vue en lecture d'un agrégat jour×taux de l'e-reporting de paiement (projection
/// <c>pipeline.payment_aggregations</c>, PIP03a — F09 §2). Surface de lecture publiée par le module
/// Pipeline, consommée par <c>GET /payments</c> (API01b) et la page Encaissements (WEB06). C'est une
/// PROJECTION RECALCULÉE (read-model), PRÉ-fenêtrage : aucune période déclarative ni état de transmission
/// (PIP03b) — seulement la qualification fiscale calculée par PIP03a. Montants en <see cref="decimal"/>
/// (CLAUDE.md n°1), pouvant être négatifs (remboursement — F09 §5.4).
/// </summary>
public sealed record PaymentDailyAggregateDto
{
    /// <summary>Identifiant de la ligne de projection (clé d'affichage côté console).</summary>
    public required Guid Id { get; init; }

    /// <summary>Jour d'encaissement agrégé.</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Taux de TVA de la ventilation (decimal).</summary>
    public required decimal VatRate { get; init; }

    /// <summary>Base taxable HT encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal TaxableBase { get; init; }

    /// <summary>TVA encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal VatAmount { get; init; }

    /// <summary>
    /// Qualification fiscale persistée par NOM (miroir de <c>PaymentAggregationStatus</c>, Domain) :
    /// <c>Calculated</c> (transmissible), <c>Suspended</c> (décision fiscale en attente),
    /// <c>NotRequired</c> (TVA sur les débits) ou <c>PendingCapability</c> (la PA ne déclare pas encore
    /// la transmission des paiements). Persistée en texte pour la lisibilité d'audit (comme
    /// <c>PaymentAggregateDto.State</c>).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Message opérateur quand l'agrégat n'est pas transmissible (CLAUDE.md n°12) ; <c>null</c> si <c>Calculated</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Instant de calcul de la projection (UTC).</summary>
    public required DateTimeOffset ComputedUtc { get; init; }
}
