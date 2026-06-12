namespace Liakont.Modules.FleetSupervision.Contracts.DTOs;

/// <summary>
/// Alerte de flotte calculée (OPS04) : instance muette, sauvegarde en échec ou version obsolète. Dérivée de
/// la télémétrie + des seuils — jamais persistée (à la différence des alertes tenant du module Supervision).
/// </summary>
public sealed record FleetAlertDto
{
    /// <summary>Instance concernée.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage de l'instance concernée.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Nature de l'alerte.</summary>
    public FleetAlertKind Kind { get; init; }

    /// <summary>Gravité.</summary>
    public FleetAlertSeverity Severity { get; init; }

    /// <summary>Message opérateur en français (action corrective implicite).</summary>
    public string Message { get; init; } = string.Empty;
}
