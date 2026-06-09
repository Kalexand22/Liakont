namespace Liakont.Modules.Supervision.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Récapitulatif quotidien (digest) optionnel des alertes ACTIVES du tenant courant, envoyé à l'opérateur
/// d'instance (SUP03 §3, F12 §5.3). Appelé par le job tenant de digest dans le scope du tenant ; gardé par
/// <c>SupervisionNotificationOptions.DailyDigestEnabled</c>. Ne lève jamais (fire-and-log).
/// </summary>
public interface IAlertDigestSender
{
    /// <summary>
    /// Envoie le digest des alertes actives du tenant courant à l'opérateur, si le digest est activé et
    /// qu'au moins une alerte est active. Sans effet sinon.
    /// </summary>
    Task SendActiveAlertsDigestAsync(string tenantId, CancellationToken cancellationToken = default);
}
