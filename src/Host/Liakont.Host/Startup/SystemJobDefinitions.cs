namespace Liakont.Host.Startup;

using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Reconciliation.Infrastructure;
using Liakont.Modules.Signature.Infrastructure.Drain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.SupportTrace.Infrastructure;

/// <summary>
/// Source UNIQUE des jobs SYSTÈME récurrents de fan-out de la plateforme (FIX203b, étendue RDL07/A6-cons-2),
/// partagée par l'amorçage des planifications en dev (<see cref="DevJobScheduleSeeder"/>, classe
/// <see cref="SystemJobClass.RequiredSeeded"/> uniquement) et le diagnostic de démarrage
/// (<see cref="SystemJobScheduleHealthCheck"/>, qui couvre TOUTES les classes).
/// <para>
/// DEUX classes (RDL07/A6-cons-2) :
/// <list type="bullet">
///   <item><see cref="SystemJobClass.RequiredSeeded"/> : cadence SOURCÉE — évaluation de la supervision =
///     toutes les 15 min (F12 §5.1), ancrage quotidien du coffre WORM = quotidien (TRK06, ADR-0011). AUCUNE
///     n'est inventée. Amorcées en dev.</item>
///   <item><see cref="SystemJobClass.DeploymentCadence"/> : récurrentes MAIS cadence = geste de déploiement
///     (aucune n'est sourcée → aucune n'est inventée : <c>CronExpression = null</c>, non amorcées). Les
///     déclarer ICI permet au diagnostic de démarrage de signaler un job de fan-out jamais planifié
///     (« job mort en prod ») au lieu d'un faux-vert silencieux.</item>
/// </list>
/// </para>
/// <para>
/// HORS périmètre (volontairement absents) : le récapitulatif de supervision (SUP03, OPTIONNEL, défaut
/// désactivé — opt-in opérateur, un avertissement serait du bruit) ; la télémétrie d'instance et la
/// notification de flotte (méta-supervision OPS04, opt-in, NON des fan-out par tenant) ; les jobs
/// transactionnels (envoi d'e-mail, relance) et le déclencheur d'envoi MONO-tenant (action de console),
/// qui ne sont pas des fan-out récurrents. Les expressions cron sont interprétées en UTC (Cronos).
/// </para>
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
        new SystemJobDefinition(
            JobType: typeof(SendAllTrigger).FullName!,
            ScheduleName: "Envoi des documents (tous les tenants)",
            CronExpression: null,
            Label: "Envoi des documents prêts, tous les tenants (PIP01c)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(SyncAllTrigger).FullName!,
            ScheduleName: "Synchronisation des comptes rendus (tous les tenants)",
            CronExpression: null,
            Label: "Synchronisation des comptes rendus DGFiP + addenda WORM (PIP01d)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(AggregatePaymentsAllTrigger).FullName!,
            ScheduleName: "Agrégation des encaissements (tous les tenants)",
            CronExpression: null,
            Label: "Agrégation des encaissements, projection jour×taux (PIP03a)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(RectifyReportsAllTrigger).FullName!,
            ScheduleName: "Rectificatifs e-reporting (tous les tenants)",
            CronExpression: null,
            Label: "Rectificatifs e-reporting annule-et-remplace (PIP04)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(AggregateB2cMarginAllTrigger).FullName!,
            ScheduleName: "E-reporting B2C de la marge (tous les tenants)",
            CronExpression: null,
            Label: "E-reporting B2C de la marge, agrégation jour×devise×taux + transmission (B4)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(AggregateB2cTaxableAllTrigger).FullName!,
            ScheduleName: "E-reporting B2C au régime du prix total (tous les tenants)",
            CronExpression: null,
            Label: "E-reporting B2C au régime du prix total taxable, agrégation jour×devise×taux + transmission (TLB1, BUG-8)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(ReconciliationFanOutJobPayload).FullName!,
            ScheduleName: "Rapprochement des PDF (réconciliation)",
            CronExpression: null,
            Label: "Rapprochement des PDF reçus aux documents émis (TRK07)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(SupportTracePurgeTrigger).FullName!,
            ScheduleName: "Purge de la trace de support du Factur-X",
            CronExpression: null,
            Label: "Purge de la trace de support du Factur-X (FX06, rétention courte)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(SelfBilledAcceptanceTacitTrigger).FullName!,
            ScheduleName: "Bascule tacite des acceptations d'auto-factures",
            CronExpression: null,
            Label: "Bascule tacite des acceptations d'auto-factures 389 (MND04, ADR-0024)",
            Class: SystemJobClass.DeploymentCadence),
        new SystemJobDefinition(
            JobType: typeof(SignatureWebhookDrainTrigger).FullName!,
            ScheduleName: "Drain des webhooks de signature (rapatriement WORM)",
            CronExpression: null,
            Label: "Drain des webhooks de signature, rapatriement WORM (SIG09)",
            Class: SystemJobClass.DeploymentCadence),
    ];
}
