namespace Liakont.Modules.FleetSupervision.Contracts.DTOs;

using System.Collections.Generic;

/// <summary>
/// Vue d'ensemble de la flotte pour le dashboard d'IT Innovations (OPS04) : toutes les instances connues, la
/// dernière version publiée (paramétrage du central) et les alertes calculées (instance muette / sauvegarde
/// en échec / version obsolète).
/// </summary>
public sealed record FleetOverviewDto
{
    /// <summary>Dernière version de plateforme publiée (référence de l'alerte « version obsolète »).</summary>
    public string LatestVersion { get; init; } = string.Empty;

    /// <summary>Instances connues de la flotte.</summary>
    public IReadOnlyList<FleetInstanceDto> Instances { get; init; } = [];

    /// <summary>Alertes calculées sur l'ensemble de la flotte.</summary>
    public IReadOnlyList<FleetAlertDto> Alerts { get; init; } = [];

    /// <summary>Horodatage UTC de génération de la vue.</summary>
    public DateTimeOffset GeneratedUtc { get; init; }
}
