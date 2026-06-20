namespace Liakont.Agent.Contracts.Pivot;

using System;

/// <summary>
/// Période de facturation du document (EN 16931 BG-14 : <c>StartDate</c> = BT-73, <c>EndDate</c> = BT-74).
/// Slot RÉSERVÉ pour les flux d'abonnement / usage (SaaS metered, lignes calculées sur une période) —
/// ADR-0004 D4 Famille 3 / §5, RD406 : un emplacement nommé dans le contrat pour qu'un futur connecteur
/// d'abonnement soit un <i>ajout</i>, jamais une <i>rupture</i>. DTO PUR : aucune règle, aucun calcul ;
/// porté tel quel par la source (CLAUDE.md n°2 — jamais inventé). Inerte en V1 : aucun sérialiseur PA ne
/// le projette encore (champ optionnel nul par défaut → hash canonique inchangé tant qu'il est absent).
/// </summary>
public sealed class PivotInvoicePeriodDto
{
    /// <summary>Crée une période de facturation.</summary>
    /// <param name="startDate">Début de la période de facturation (EN 16931 BT-73).</param>
    /// <param name="endDate">Fin de la période de facturation (EN 16931 BT-74).</param>
    public PivotInvoicePeriodDto(DateTime startDate, DateTime endDate)
    {
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Début de la période de facturation (EN 16931 BT-73).</summary>
    public DateTime StartDate { get; }

    /// <summary>Fin de la période de facturation (EN 16931 BT-74).</summary>
    public DateTime EndDate { get; }
}
