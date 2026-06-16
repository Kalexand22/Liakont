namespace Liakont.Modules.SupportTrace.Infrastructure;

/// <summary>
/// Déclencheur du job SYSTÈME de purge de la trace de support (FX06). Son handler
/// <see cref="SupportTracePurgeFanOutHandler"/> fait le fan-out sur tous les tenants via le runner. Marqueur
/// sans donnée : la purge agit sur la fenêtre de rétention configurée de chaque tenant. La PLANIFICATION
/// (cron) est un geste opérateur via l'admin des planifications (le déploiement choisit la cadence —
/// housekeeping d'une rétention courte) : aucune cadence n'est inventée ici.
/// </summary>
public sealed record SupportTracePurgeTrigger;
