namespace Liakont.Agent.Updater;

using System;

/// <summary>
/// Séquence de remplacement des binaires avec rollback (ADR-0013, AGT04) : arrêt du service →
/// sauvegarde → remplacement → démarrage → ATTENTE du redémarrage SAIN. Si la santé n'est pas
/// confirmée dans le délai (ou en cas d'exception), RESTAURE l'ancienne version et la redémarre.
/// Toute la logique est ici, derrière des coutures (<see cref="IServiceControl"/>,
/// <see cref="IBinarySwapper"/>, <see cref="IServiceHealthProbe"/>) — testable sans vrai SCM ni I/O.
/// </summary>
public sealed class UpdaterEngine
{
    private readonly IServiceControl _service;
    private readonly IBinarySwapper _swapper;
    private readonly IServiceHealthProbe _health;
    private readonly IUpdaterLog _log;

    /// <summary>Crée un moteur d'updater.</summary>
    /// <param name="service">Pilotage du service Windows.</param>
    /// <param name="swapper">Sauvegarde / remplacement / restauration des binaires.</param>
    /// <param name="health">Sonde de santé du redémarrage.</param>
    /// <param name="log">Journal de l'updater.</param>
    public UpdaterEngine(IServiceControl service, IBinarySwapper swapper, IServiceHealthProbe health, IUpdaterLog log)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _swapper = swapper ?? throw new ArgumentNullException(nameof(swapper));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Exécute le remplacement ; restaure l'ancienne version si le redémarrage n'est pas sain.</summary>
    /// <param name="plan">Le plan de remplacement.</param>
    /// <returns>L'issue (appliqué / rollback / échec).</returns>
    public UpdaterResult Run(UpdaterPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        try
        {
            _log.Write($"Mise à jour vers {plan.TargetVersion} : arrêt du service {plan.ServiceName}.");
            _service.StopService(plan.ServiceName, plan.HealthTimeout);

            _log.Write("Sauvegarde des binaires courants (conservés pour un éventuel rollback).");
            _swapper.Backup(plan.InstallDirectory, plan.BackupDirectory);

            _log.Write("Remplacement des binaires par la nouvelle version.");
            _swapper.Apply(plan.StagingDirectory, plan.InstallDirectory);

            _log.Write("Démarrage du service avec la nouvelle version.");
            _service.StartService(plan.ServiceName, plan.HealthTimeout);

            _log.Write("Attente du redémarrage sain (service en cours + heartbeat local frais).");
            if (_health.WaitUntilHealthy(plan.ServiceName, plan.HeartbeatMarkerPath, plan.HealthTimeout))
            {
                string applied = $"Mise à jour appliquée : la version {plan.TargetVersion} a redémarré sainement.";
                _log.Write(applied);
                return new UpdaterResult(UpdaterOutcome.Applied, applied);
            }

            _log.Write("Redémarrage non confirmé sain dans le délai imparti → rollback.");
            return Rollback(plan, "la nouvelle version n'a pas redémarré sainement dans le délai imparti");
        }
        catch (Exception ex)
        {
            _log.Write("Échec pendant la mise à jour : " + ex.Message + " → rollback.");
            return Rollback(plan, "échec pendant la mise à jour (" + ex.Message + ")");
        }
    }

    private UpdaterResult Rollback(UpdaterPlan plan, string reason)
    {
        try
        {
            TryStopBeforeRestore(plan);
            _swapper.Restore(plan.BackupDirectory, plan.InstallDirectory);
            _service.StartService(plan.ServiceName, plan.HealthTimeout);
            string rolledBack = $"Rollback effectué : l'ancienne version a été restaurée et redémarrée ({reason}).";
            _log.Write(rolledBack);
            return new UpdaterResult(UpdaterOutcome.RolledBack, rolledBack);
        }
        catch (Exception ex)
        {
            string failed = $"ÉCHEC du rollback ({reason}) : {ex.Message}. Intervention manuelle requise.";
            _log.Write(failed);
            return new UpdaterResult(UpdaterOutcome.Failed, failed);
        }
    }

    private void TryStopBeforeRestore(UpdaterPlan plan)
    {
        try
        {
            _service.StopService(plan.ServiceName, plan.HealthTimeout);
        }
        catch (Exception ex)
        {
            // Le service peut déjà être arrêté/non démarrable : la restauration peut tout de même se faire.
            _log.Write("Arrêt avant restauration ignoré : " + ex.Message);
        }
    }
}
