namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Stratum.Modules.Notification.Contracts;

/// <summary>
/// Envoie l'email « nouvelle version disponible » (OPS04) à une instance self-hosted en retard, via le
/// transport email du Host (<see cref="IEmailTransport"/>, vendored, non modifié). Le module ne porte AUCUN
/// secret SMTP : le mot de passe vit dans le transport, côté Host (CLAUDE.md n°10/18). Si le transport n'est
/// pas configuré, l'envoi est un no-op journalisé (comportement du transport) — jamais une exception.
/// </summary>
internal sealed class EmailFleetUpdateNotificationSender : IFleetUpdateNotificationSender
{
    private readonly IEmailTransport _emailTransport;

    public EmailFleetUpdateNotificationSender(IEmailTransport emailTransport)
    {
        _emailTransport = emailTransport;
    }

    public Task SendNewVersionAvailableAsync(
        string contactEmail,
        string instanceDisplayName,
        string currentVersion,
        string latestVersion,
        CancellationToken cancellationToken = default)
    {
        CultureInfo fr = CultureInfo.GetCultureInfo("fr-FR");

        string subject = string.Format(fr, "Liakont — nouvelle version disponible ({0})", latestVersion);

        const string bodyTemplate =
            "Bonjour,\n\n"
            + "Une nouvelle version de la plateforme Liakont est disponible.\n"
            + "Votre instance « {0} » exécute actuellement la version {1} ; la dernière version publiée est {2}.\n\n"
            + "Veuillez planifier la mise à jour de votre instance. Les notes de version sont mises à votre "
            + "disposition par IT Innovations.\n";

        string currentLabel = string.IsNullOrWhiteSpace(currentVersion) ? "inconnue" : currentVersion;
        string body = string.Format(fr, bodyTemplate, instanceDisplayName, currentLabel, latestVersion);

        return _emailTransport.SendAsync(contactEmail, subject, body, cancellationToken);
    }
}
