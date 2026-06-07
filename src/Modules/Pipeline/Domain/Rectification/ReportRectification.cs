namespace Liakont.Modules.Pipeline.Domain.Rectification;

using System;
using System.Collections.Generic;

/// <summary>
/// Agrégat rectifié COMPLET d'une période (PIP04, flux RE — F07-F08 §B.1) : l'<b>intégralité</b> des lignes
/// reportables de la période (par jour × taux), pas un delta. La rectification d'e-reporting « annule et
/// remplace » l'ensemble des données agrégées de la période (par SIREN + période) — c'est donc la photo
/// complète et corrigée qui est (re)transmise, jamais un montant négatif isolé.
/// </summary>
/// <remarks>
/// <see cref="ContentHash"/> est une empreinte DÉTERMINISTE du contenu rectifié (bornes + lignes, montants en
/// <see cref="decimal"/> canonisés — jamais de float, CLAUDE.md n°1) : elle est la clé d'IDEMPOTENCE du
/// mécanisme (re-déclencher un rectificatif au contenu identique ne re-transmet pas — PIP04 §4). Une période
/// sans ligne reportable produit un agrégat VIDE (<see cref="IsEmpty"/>) : un « annule » total reste un fait
/// d'audit, mais le service ne transmet rien s'il n'y a jamais eu de déclaration à annuler.
/// </remarks>
public sealed record ReportRectification
{
    /// <summary>Premier jour de la période couverte (inclus).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>Dernier jour de la période couverte (inclus).</summary>
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>Lignes jour×taux reportables de la période, triées (jour puis taux) — la déclaration complète.</summary>
    public required IReadOnlyList<RectificationLine> Lines { get; init; }

    /// <summary>Empreinte déterministe du contenu rectifié (clé d'idempotence) — hex SHA-256 minuscule.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Vrai si la période ne porte aucune ligne reportable (annule total).</summary>
    public bool IsEmpty => Lines.Count == 0;
}
