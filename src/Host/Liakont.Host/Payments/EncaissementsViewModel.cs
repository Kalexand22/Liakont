namespace Liakont.Host.Payments;

using System.Collections.Generic;

/// <summary>
/// Données présentationnelles de la page Encaissements (WEB06, F10 §2.4), assemblées par
/// <see cref="EncaissementsConsoleQueryService"/> à partir des lectures Contracts (agrégats PIP03a via
/// <c>GET /payments</c> + paramétrage du tenant via <c>GET /settings</c>) et rendues par la page. Modèle PUR
/// (aucune dépendance DI, aucune logique métier) pour rester testable en bUnit sans authentification ni base.
/// L'affichage adapté de F10 §2.4 (les 3 états) est piloté par <see cref="FiscalDecisionPending"/> (paramètre
/// fiscal en attente) et <see cref="PaymentReportingSupported"/> (capacité de la PA) — JAMAIS par un
/// <c>if (pa is …)</c> ni une règle fiscale dérivée (CLAUDE.md n°2/8).
/// </summary>
internal sealed record EncaissementsViewModel
{
    /// <summary>Agrégats jour×taux du tenant pour la période (vide si aucun encaissement agrégé).</summary>
    public required IReadOnlyList<PaymentAggregateRow> Aggregates { get; init; }

    /// <summary>
    /// <c>true</c> si une décision fiscale requise pour l'e-reporting de paiement est en attente (TVA sur les
    /// débits ou catégorie d'opération non renseignée) — contrôle de COMPLÉTUDE du paramétrage, pas une règle
    /// fiscale dérivée (CLAUDE.md n°2). Déclenche le bandeau « Décision fiscale en attente ».
    /// </summary>
    public required bool FiscalDecisionPending { get; init; }

    /// <summary><c>true</c> si au moins une PA configurée déclare la transmission des paiements domestiques (flux 10.4).</summary>
    public required bool PaymentReportingSupported { get; init; }

    /// <summary><c>true</c> si au moins un compte PA est configuré pour le tenant.</summary>
    public required bool HasConfiguredPa { get; init; }

    /// <summary>Nom de la PA configurée (pour le bandeau de capacité), ou <c>null</c> si aucune PA configurée.</summary>
    public string? PaName { get; init; }
}
