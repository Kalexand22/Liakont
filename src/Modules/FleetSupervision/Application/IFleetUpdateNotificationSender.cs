namespace Liakont.Modules.FleetSupervision.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Envoie l'email « nouvelle version disponible » à une instance self-hosted en retard (OPS04). Abstrait le
/// transport email du Host (le module ne porte AUCUN secret SMTP). Implémenté en Infrastructure via
/// <c>IEmailTransport</c> du module Notification.
/// </summary>
public interface IFleetUpdateNotificationSender
{
    /// <summary>Notifie le contact technique d'une instance qu'une version plus récente est disponible.</summary>
    Task SendNewVersionAvailableAsync(
        string contactEmail,
        string instanceDisplayName,
        string currentVersion,
        string latestVersion,
        CancellationToken cancellationToken = default);
}
