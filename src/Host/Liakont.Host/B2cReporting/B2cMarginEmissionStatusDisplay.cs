namespace Liakont.Host.B2cReporting;

using Stratum.Common.UI.Models;

/// <summary>
/// Vocabulaire opérateur FRANÇAIS du statut d'émission d'un agrégat e-reporting B2C de la marge (page des
/// émissions marge). Indexé par le NOM du statut porté par
/// <see cref="Liakont.Modules.Pipeline.Contracts.B2cMarginEmissionAggregateDto.Status"/> (issue B4), ce qui
/// garde l'affichage DÉCOUPLÉ de l'énumération du domaine Pipeline (<c>B2cMarginEmissionStatus</c>). Pur
/// affichage : le statut n'est jamais (re)dérivé ici, il est seulement traduit et coloré (CLAUDE.md n°2).
/// </summary>
public static class B2cMarginEmissionStatusDisplay
{
    /// <summary>
    /// Libellé français et sévérité de badge pour un statut d'émission. Fonction TOTALE : un statut inconnu
    /// (ou vide) retombe sur un libellé neutre — jamais d'exception, jamais de couleur trompeuse.
    /// </summary>
    /// <param name="status">Nom du statut (ex. <c>Issued</c>), tel que produit par B4.</param>
    public static (string Label, Severity Severity) For(string? status) => status switch
    {
        // Agrégat CRÉÉ côté PA (HTTP 200) — succès terminal.
        "Issued" => ("Émis", Severity.Success),

        // Tentative engagée (Pending écrit AVANT le POST) : issue encore inconnue. Un agrégat qui reste dans
        // cet état signale un POST interrompu (crash) — l'opérateur doit vérifier avant toute reprise.
        "Pending" => ("Transmission engagée", Severity.Warning),

        // Rejet métier de la PA (4xx) — rien créé, non ré-émis en auto (correction de données = geste opérateur).
        "RejectedByPa" => ("Rejeté par la plateforme", Severity.Error),

        // Échec technique/transitoire (timeout, 5xx, réseau, auth) — issue incertaine, non ré-émis en auto.
        "Technical" => ("Échec technique", Severity.Error),

        _ => (string.IsNullOrWhiteSpace(status) ? "—" : status!, Severity.Neutral),
    };
}
