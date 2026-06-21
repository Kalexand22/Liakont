namespace Liakont.Host.Startup;

/// <summary>
/// Classe d'un job SYSTÈME récurrent de fan-out (RDL07, A6-cons-2). Détermine le traitement par
/// l'amorçage de dev et la sévérité/le message du diagnostic de démarrage.
/// </summary>
internal enum SystemJobClass
{
    /// <summary>
    /// REQUIS sur toute instance, à cadence SOURCÉE (spec) : sa <see cref="SystemJobDefinition.CronExpression"/>
    /// vient de la spec et est amorcée en dev (<see cref="DevJobScheduleSeeder"/>). Exemples : évaluation de la
    /// supervision (F12 §5.1) et ancrage quotidien du coffre WORM (TRK06, ADR-0011). Absent au démarrage ⇒
    /// avertissement « doit être planifié » (la plateforme est aveugle sans lui).
    /// </summary>
    RequiredSeeded,

    /// <summary>
    /// RÉCURRENT mais dont la CADENCE relève du DÉPLOIEMENT (aucune n'est sourcée — donc aucune n'est inventée :
    /// pas de <see cref="SystemJobDefinition.CronExpression"/>, pas d'amorçage de dev). Attendu UNIQUEMENT si la
    /// fonctionnalité correspondante est utilisée (envoi, rapprochement, purge de trace, drain de signature,
    /// bascule tacite 389…). Absent au démarrage ⇒ avertissement conditionnel (« à planifier si vous utilisez
    /// cette fonctionnalité »), pour qu'un job de fan-out jamais planifié ne reste pas un faux-vert (A6-cons-2).
    /// </summary>
    DeploymentCadence,
}

/// <summary>
/// Définition d'un job SYSTÈME récurrent de la plateforme (fan-out sur tous les tenants par le
/// runner SOL06). <see cref="JobType"/> est le nom complet du type de déclencheur — la clé technique
/// résolue par <c>JobHandlerResolver</c> et stockée dans <c>job.schedules.job_type</c>.
/// <see cref="CronExpression"/> n'est renseignée que pour la classe <see cref="SystemJobClass.RequiredSeeded"/>
/// (cadence sourcée + amorcée en dev) ; elle vaut <c>null</c> pour <see cref="SystemJobClass.DeploymentCadence"/>
/// (la cadence est un geste de déploiement, aucune n'est inventée — RDL07/A6-cons-2).
/// </summary>
internal sealed record SystemJobDefinition(
    string JobType,
    string ScheduleName,
    string? CronExpression,
    string Label,
    SystemJobClass Class = SystemJobClass.RequiredSeeded);
