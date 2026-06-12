namespace Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Seuils d'évaluation des alertes de flotte (OPS04), issus du paramétrage du central
/// (<see cref="FleetCentralOptions"/>). Aucune règle fiscale ici — uniquement de l'exploitation technique.
/// </summary>
/// <param name="InstanceMuteThresholdMinutes">Silence (minutes) au-delà duquel une instance est muette.</param>
/// <param name="BackupMaxAgeHours">Âge maximal (heures) d'une sauvegarde réussie avant alerte.</param>
public readonly record struct FleetAlertThresholds(int InstanceMuteThresholdMinutes, int BackupMaxAgeHours);
