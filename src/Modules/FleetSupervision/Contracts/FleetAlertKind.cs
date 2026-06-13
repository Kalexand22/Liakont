namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Nature d'une alerte de flotte (OPS04) — dérivée de la télémétrie d'instance, jamais persistée comme
/// état mutable (à la différence des alertes tenant du module Supervision) : une fonction pure de l'état
/// rapporté + des seuils de la flotte.
/// </summary>
public enum FleetAlertKind
{
    /// <summary>Instance muette : aucun heartbeat reçu depuis plus que le seuil de silence.</summary>
    InstanceMute = 0,

    /// <summary>Sauvegarde en échec : aucune sauvegarde réussie connue, ou trop ancienne.</summary>
    BackupFailure = 1,

    /// <summary>Version obsolète : l'instance est en retard sur la dernière version publiée.</summary>
    ObsoleteVersion = 2,
}
