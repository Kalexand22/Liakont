namespace Liakont.Agent.Core.Update;

using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Service d'auto-update de l'agent (AGT04, F12 §2.5). Câblé comme une COUTURE optionnelle dans le
/// heartbeat (déclencheur <c>updateRequired</c>) et le cycle de run (déclencheur 426) : ni l'un ni
/// l'autre n'embarque la logique de mise à jour, ils SIGNALENT au service qui décide. Aucune méthode
/// ne lève — le contrat « jamais d'exception sur un thread de fond » de l'agent s'applique (F12 §2.5).
/// </summary>
public interface IAutoUpdateService
{
    /// <summary>
    /// Examine la configuration renvoyée par la plateforme : si une mise à jour est requise
    /// (<see cref="AgentConfigurationDto.UpdateRequired"/> OU un 426 a été signalé), prépare et lance
    /// la mise à jour (téléchargement → vérif signature + hash → updater détaché), en différant tant
    /// qu'un run d'extraction est en cours.
    /// </summary>
    /// <param name="configuration">La configuration effective reçue de la plateforme.</param>
    /// <returns>Le résultat mécanique de la tentative.</returns>
    AutoUpdateResult ConsiderHeartbeatConfiguration(AgentConfigurationDto configuration);

    /// <summary>
    /// Mémorise qu'un push a reçu un 426 (version non supportée). Le run en cours détient le verrou,
    /// la mise à jour ne peut pas partir tout de suite : on pose un fanion consommé au prochain cycle
    /// de heartbeat (hors run).
    /// </summary>
    void RecordPushUpgradeRequired();

    /// <summary>Dernier statut connu d'auto-update (pour le signalement au heartbeat), ou <c>null</c>.</summary>
    /// <returns>Le statut, ou <c>null</c>.</returns>
    AutoUpdateStatus? GetLatestStatus();
}
