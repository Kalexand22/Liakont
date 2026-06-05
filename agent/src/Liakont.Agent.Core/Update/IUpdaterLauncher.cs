namespace Liakont.Agent.Core.Update;

/// <summary>
/// Lance l'updater DÉTACHÉ (ADR-0013) : un processus séparé qui survit à l'arrêt du service et ne
/// tourne JAMAIS depuis le dossier qu'il remplace. Couture testable : un test vérifie qu'on a (ou non)
/// lancé l'updater, sans démarrer de vrai processus.
/// </summary>
public interface IUpdaterLauncher
{
    /// <summary>
    /// Lance l'updater pour appliquer la mise à jour décrite par <paramref name="request"/>. Renvoie
    /// <c>true</c> si le processus a été démarré (à partir de quoi le remplacement se poursuit hors de
    /// l'agent), <c>false</c> si le lancement a échoué.
    /// </summary>
    /// <param name="request">Paramètres de remplacement/rollback.</param>
    /// <returns><c>true</c> si l'updater détaché a démarré.</returns>
    bool Launch(UpdaterLaunchRequest request);
}
