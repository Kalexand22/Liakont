namespace Liakont.Modules.Archive.Infrastructure;

/// <summary>
/// Déclencheur du job SYSTÈME d'ancrage quotidien (TRK06). Planifié par le module <c>Job</c> (JobScheduler,
/// base système) ; son handler <see cref="DailyAnchoringFanOutHandler"/> fait le fan-out sur tous les
/// tenants via le runner. Marqueur sans donnée : l'ancrage agit sur la tête de chaîne courante de chaque tenant.
/// </summary>
public sealed record DailyAnchoringTrigger;
