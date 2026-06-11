namespace Liakont.Host.Startup;

/// <summary>
/// Définition d'un job SYSTÈME récurrent de la plateforme (fan-out sur tous les tenants par le
/// runner SOL06). <see cref="JobType"/> est le nom complet du type de déclencheur — la clé technique
/// résolue par <c>JobHandlerResolver</c> et stockée dans <c>job.schedules.job_type</c>.
/// </summary>
internal sealed record SystemJobDefinition(string JobType, string ScheduleName, string CronExpression, string Label);
