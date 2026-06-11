namespace Liakont.Modules.Supervision.Infrastructure;

/// <summary>
/// Déclencheur du dead-man's-switch (F12 §5, item SUP01a). Planifié par le module <c>Job</c> (JobScheduler,
/// base système) — la fréquence (toutes les 15 min, F12 §5.1) est un paramétrage de déploiement créé par
/// l'opérateur via l'admin des schedules, comme l'ancrage quotidien (TRK06). Son handler
/// <see cref="SupervisionEvaluationFanOutHandler"/> fait le fan-out de l'évaluation sur tous les tenants
/// via le runner. Marqueur sans donnée : l'évaluation agit sur l'état courant de chaque tenant.
/// </summary>
public sealed record SupervisionEvaluationTrigger;
