namespace Liakont.Agent.Core.Heartbeat;

/// <summary>
/// Origine de la planification d'extraction effective (F12 §6.1 décision n°3 : la plateforme peut
/// surcharger le fichier local).
/// </summary>
public enum ExtractionScheduleSource
{
    /// <summary>La planification du fichier local <c>agent.json</c> gouverne (plateforme silencieuse).</summary>
    Local = 0,

    /// <summary>La planification poussée par la plateforme gouverne (elle surcharge le fichier local).</summary>
    Platform = 1,
}
