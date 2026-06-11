namespace Liakont.Host.Startup;

using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Supervision.Infrastructure;

/// <summary>
/// Source UNIQUE des jobs SYSTÈME attendus de la plateforme (FIX203b), partagée par l'amorçage des
/// planifications en dev (<see cref="DevJobScheduleSeeder"/>) et le diagnostic de démarrage
/// (<see cref="SystemJobScheduleHealthCheck"/>). Les cadences viennent des specs — AUCUNE n'est
/// inventée : évaluation de la supervision = toutes les 15 min (F12 §5.1), ancrage quotidien du
/// coffre WORM = quotidien (TRK06, ADR-0011). Les expressions cron sont interprétées en UTC (Cronos).
/// Le digest (SUP03, OPTIONNEL, défaut désactivé) n'est PAS amorcé : il reste un geste opérateur.
/// </summary>
internal static class SystemJobDefinitions
{
    public static readonly IReadOnlyList<SystemJobDefinition> All =
    [
        new SystemJobDefinition(
            JobType: typeof(SupervisionEvaluationTrigger).FullName!,
            ScheduleName: "Évaluation de la supervision",
            CronExpression: "*/15 * * * *",
            Label: "Évaluation de la supervision (dead-man's-switch, F12 §5.1)"),
        new SystemJobDefinition(
            JobType: typeof(DailyAnchoringTrigger).FullName!,
            ScheduleName: "Ancrage quotidien du coffre d'archive",
            CronExpression: "0 0 * * *",
            Label: "Ancrage quotidien du coffre d'archive (TRK06, ADR-0011)"),
    ];
}
