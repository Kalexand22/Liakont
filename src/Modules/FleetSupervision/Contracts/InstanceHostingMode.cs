namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Mode d'hébergement d'une instance de la plateforme (OPS04, F12 §6.3). Pilote la notification de mise à
/// jour : une instance <see cref="SelfHosted"/> en retard reçoit un email « nouvelle version disponible »
/// (l'éditeur applique lui-même la mise à jour) ; une instance <see cref="Operated"/> est mise à jour par
/// IT Innovations et n'est que signalée au dashboard de flotte.
/// </summary>
public enum InstanceHostingMode
{
    /// <summary>Instance opérée par IT Innovations (hébergement dédié / mutualisé).</summary>
    Operated = 0,

    /// <summary>Instance auto-hébergée par l'éditeur (self-hosted).</summary>
    SelfHosted = 1,
}
