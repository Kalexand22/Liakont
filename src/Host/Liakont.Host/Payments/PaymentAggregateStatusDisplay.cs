namespace Liakont.Host.Payments;

using Stratum.Common.UI.Models;

/// <summary>
/// Vocabulaire opérateur FRANÇAIS de la qualification fiscale d'un agrégat jour×taux de l'e-reporting de
/// paiement (page Encaissements, WEB06). Indexé par le NOM du statut porté par
/// <see cref="Liakont.Modules.Pipeline.Contracts.PaymentDailyAggregateDto.Status"/> (calculé par PIP03a),
/// ce qui garde l'affichage DÉCOUPLÉ de l'énumération du domaine Pipeline. Pur affichage : la qualification
/// n'est jamais (re)dérivée ici, elle est seulement traduite et colorée (CLAUDE.md n°2). Le libellé décrit
/// la QUALIFICATION de l'agrégat (transmissible / suspendu / non concerné / en attente de capacité PA) —
/// pas un état de transmission réel (transmis/rejeté), qui relève de PIP03b (hors périmètre WEB06).
/// </summary>
public static class PaymentAggregateStatusDisplay
{
    /// <summary>
    /// Libellé français et sévérité de badge pour un statut d'agrégat. Fonction TOTALE : un statut inconnu
    /// (ou vide) retombe sur un libellé neutre — jamais d'exception, jamais de couleur trompeuse.
    /// </summary>
    /// <param name="status">Nom du statut (ex. <c>Calculated</c>), tel que produit par PIP03a.</param>
    public static (string Label, Severity Severity) For(string? status) => status switch
    {
        // Agrégat qualifié et transmissible : il partira par l'e-reporting de paiement (flux 10.4).
        "Calculated" => ("À transmettre", Severity.Info),

        // Décision fiscale manquante (catégorie d'opération / TVA sur les débits) : agrégat suspendu (F09 §2).
        "Suspended" => ("Décision fiscale en attente", Severity.Warning),

        // TVA sur les débits : l'e-reporting de paiement n'est pas requis pour cet agrégat (non concerné).
        "NotRequired" => ("Non concerné (TVA sur les débits)", Severity.Neutral),

        // La PA configurée ne déclare pas (encore) la transmission des paiements : agrégat prêt, en attente.
        "PendingCapability" => ("En attente (plateforme)", Severity.Warning),

        _ => (string.IsNullOrWhiteSpace(status) ? "—" : status!, Severity.Neutral),
    };
}
