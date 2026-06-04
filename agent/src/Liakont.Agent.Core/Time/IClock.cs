namespace Liakont.Agent.Core.Time;

using System;

/// <summary>
/// Horloge abstraite : permet aux composants de l'agent (file locale, journal, arrêt propre) de
/// dater leurs opérations sans dépendre directement de <see cref="DateTime.UtcNow"/>, donc d'être
/// testés de façon déterministe (rétention 90 jours, rotation, fenêtres de temps).
/// </summary>
public interface IClock
{
    /// <summary>Instant courant en temps universel coordonné (UTC).</summary>
    DateTime UtcNow { get; }
}
