namespace Liakont.Modules.Supervision.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Notification des transitions d'alerte (SUP03, F12 §5.3) : le moteur d'évaluation (SUP01a) appelle
/// <see cref="NotifyRaisedAsync"/> au DÉCLENCHEMENT d'une alerte et <see cref="NotifyResolvedAsync"/> à sa
/// RÉSOLUTION. Comme le moteur ne notifie qu'aux transitions (jamais sur « déjà active »), l'anti-spam
/// (un email au déclenchement, jamais de répétition) est garanti par construction.
/// <para>
/// CONTRAT : une implémentation NE LÈVE JAMAIS. La notification est « fire-and-log » — un échec
/// (résolution de destinataire, mise en file) est journalisé, jamais propagé : un échec de notification
/// ne doit JAMAIS casser l'évaluation des règles (SUP03 §4). L'envoi SMTP réel est asynchrone (job du
/// module Notification, avec retry) ; ces méthodes ne font qu'enfiler.
/// </para>
/// </summary>
public interface IAlertNotifier
{
    /// <summary>Notifie le déclenchement d'une nouvelle alerte (opérateur = toutes ; contact tenant = critiques).</summary>
    Task NotifyRaisedAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>Notifie l'auto-résolution d'une alerte (optionnel, selon la configuration d'instance).</summary>
    Task NotifyResolvedAsync(Alert alert, CancellationToken cancellationToken = default);
}
