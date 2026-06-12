namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>Gravité d'une alerte de flotte (OPS04).</summary>
public enum FleetAlertSeverity
{
    /// <summary>Avertissement : à surveiller, sans urgence immédiate.</summary>
    Warning = 0,

    /// <summary>Critique : action requise (instance muette, etc.).</summary>
    Critical = 1,
}
