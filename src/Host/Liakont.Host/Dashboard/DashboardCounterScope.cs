namespace Liakont.Host.Dashboard;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Un périmètre de compteurs du tableau de bord (« Mois en cours », « Mois précédent », « Année en
/// cours ») : bornes de période EXPLICITES + compteurs par état calculés sur ces bornes. Les bornes
/// sont portées par le modèle (calculées par <see cref="DashboardQueryService"/> via TimeProvider,
/// jamais par la vue) pour que le lien de drill-down ouvre la liste sur EXACTEMENT le périmètre
/// compté — un compteur dont le lien montrerait moins de documents serait un faux affichage.
/// </summary>
public sealed record DashboardCounterScope
{
    /// <summary>Clé technique stable du périmètre (testids, ex. <c>current-month</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Libellé du périmètre, affiché en sous-titre (ex. « Mois en cours »).</summary>
    public required string Label { get; init; }

    /// <summary>Borne basse (incluse) de la date d'émission.</summary>
    public required DateOnly From { get; init; }

    /// <summary>Borne haute (incluse) de la date d'émission.</summary>
    public required DateOnly To { get; init; }

    /// <summary>Compteurs par état sur ce périmètre, dans l'ordre canonique (0 inclus).</summary>
    public required IReadOnlyList<DashboardStateCount> Counts { get; init; }

    /// <summary>
    /// Lien de drill-down vers la liste des documents filtrée sur <paramref name="state"/> ET sur les
    /// bornes de CE périmètre (paramètres d'URL restaurés par la page Documents — issue #33).
    /// </summary>
    public string DocumentsUrl(string state) =>
        $"/documents?etat={Uri.EscapeDataString(state)}"
        + $"&du={From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        + $"&au={To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
