namespace Liakont.Host.Dashboard;

using System;
using System.Collections.Generic;

/// <summary>
/// Données présentationnelles du tableau de bord d'accueil (WEB01), assemblées par la page
/// <c>Dashboard</c> à partir des lectures Contracts (Documents, Ingestion, TenantSettings) et rendues
/// par <c>DashboardView</c>. Modèle PUR (aucune dépendance DI, aucune logique métier) pour rester
/// testable en bUnit sans authentification ni base.
/// </summary>
public sealed record DashboardViewModel
{
    /// <summary>
    /// <c>true</c> si le profil légal du tenant (CFG02) est créé. <c>false</c> = paramétrage incomplet :
    /// le CHECK suspend tout document tant que le profil manque (bug-inbox FIX01a). La vue affiche alors
    /// un bandeau explicite « traitement suspendu » plutôt que le silence.
    /// </summary>
    public required bool ProfileConfigured { get; init; }

    /// <summary>
    /// Compteurs de documents par état sur le MOIS EN COURS (tuiles principales). Les bornes du
    /// périmètre sont portées par le scope : le drill-down ouvre la liste sur exactement ce qui
    /// est compté.
    /// </summary>
    public required DashboardCounterScope CurrentMonth { get; init; }

    /// <summary>Compteurs de documents par état sur le MOIS PRÉCÉDENT (tuiles secondaires).</summary>
    public required DashboardCounterScope PreviousMonth { get; init; }

    /// <summary>
    /// Compteurs de documents par état sur l'ANNÉE EN COURS — rendus en bas de page, en graphique
    /// (vue d'ensemble, moins prioritaire que les deux mois affichés en tuiles).
    /// </summary>
    public required DashboardCounterScope CurrentYear { get; init; }

    /// <summary>Agents du tenant (vide si aucun agent enregistré).</summary>
    public required IReadOnlyList<DashboardAgentLine> Agents { get; init; }

    /// <summary>État de validation de la table de mapping TVA du tenant.</summary>
    public required DashboardTvaStatus TvaStatus { get; init; }

    /// <summary>Validateur de la table TVA, ou <c>null</c> si non validée.</summary>
    public string? TvaValidatedBy { get; init; }

    /// <summary>Date de validation de la table TVA, ou <c>null</c> si non validée.</summary>
    public DateOnly? TvaValidatedDate { get; init; }

    /// <summary>
    /// Cadence déclarative déclarée (CFG02, décision D4), ou <c>null</c>/vide si non renseignée. Chaîne
    /// OPAQUE (F12-A §3.3) : aucune date d'échéance n'est calculée (règle de cadence non sourcée → R2).
    /// </summary>
    public string? ReportingFrequency { get; init; }
}
