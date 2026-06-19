namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

/// <summary>
/// FIX203b : la décision pure du diagnostic de planification des jobs système (quels jobs attendus
/// n'ont aucun schedule actif) est testée sans E/S, comme <c>DevRealmHealthCheck.Classify</c>.
/// </summary>
public sealed class SystemJobScheduleHealthCheckTests
{
    private static SystemJobDefinition Job(string type) => new(type, type, "*/15 * * * *", type);

    [Fact]
    public void FindMissing_Should_Return_Empty_When_All_Expected_Are_Active()
    {
        var expected = new[] { Job("A"), Job("B") };
        var active = new[] { "A", "B", "C" };

        var missing = SystemJobScheduleHealthCheck.FindMissing(expected, active);

        missing.Should().BeEmpty();
    }

    [Fact]
    public void FindMissing_Should_Return_Only_Jobs_Without_Active_Schedule()
    {
        var expected = new[] { Job("A"), Job("B") };
        var active = new[] { "A" };

        var missing = SystemJobScheduleHealthCheck.FindMissing(expected, active);

        missing.Should().ContainSingle().Which.JobType.Should().Be("B");
    }

    [Fact]
    public void FindMissing_Should_Return_All_When_No_Schedule_Is_Active()
    {
        var expected = new[] { Job("A"), Job("B") };

        var missing = SystemJobScheduleHealthCheck.FindMissing(expected, Array.Empty<string>());

        missing.Should().HaveCount(2);
    }

    [Fact]
    public void FindMissing_Should_Be_Case_Sensitive_On_Job_Type()
    {
        var expected = new[] { Job("Liakont.Some.Trigger") };
        var active = new[] { "liakont.some.trigger" };

        var missing = SystemJobScheduleHealthCheck.FindMissing(expected, active);

        missing.Should().ContainSingle("le job_type est une clé technique exacte (sensible à la casse)");
    }

    [Fact]
    public void Real_System_Jobs_Should_All_Be_Missing_When_Schedules_Table_Is_Empty()
    {
        // Garde anti-régression : TOUS les vrais jobs de fan-out récurrents sont évalués (un job jamais
        // planifié = un warning au démarrage, RDL07/A6-cons-2).
        var missing = SystemJobScheduleHealthCheck.FindMissing(SystemJobDefinitions.All, Array.Empty<string>());

        missing.Should().HaveCount(SystemJobDefinitions.All.Count);
    }

    [Fact]
    public void RequiredSeeded_Jobs_Have_A_Sourced_Cron_And_DeploymentCadence_Jobs_Have_None()
    {
        // RDL07/A6-cons-2 : la cadence d'un job RequiredSeeded est SOURCÉE (cron non nul, amorçable) ; celle
        // d'un job DeploymentCadence relève du déploiement → aucun cron inventé (null).
        foreach (var job in SystemJobDefinitions.All)
        {
            if (job.Class == SystemJobClass.RequiredSeeded)
            {
                job.CronExpression.Should().NotBeNullOrWhiteSpace(
                    $"un job requis ({job.Label}) a une cadence sourcée");
            }
            else
            {
                job.CronExpression.Should().BeNull(
                    $"un job à cadence de déploiement ({job.Label}) n'invente aucun cron");
            }
        }
    }

    [Fact]
    public void All_Recurring_FanOut_Triggers_Are_Declared_In_SystemJobDefinitions()
    {
        // Lock de COUVERTURE (RDL07/A6-cons-2) : l'ensemble des jobs de fan-out récurrents déclarés est figé.
        // Câbler un nouveau fan-out récurrent SANS l'ajouter ici (le faux-vert « job mort en prod » que le
        // diagnostic doit attraper) casse ce test. Le récapitulatif SUP03 (opt-in) et la méta-supervision de
        // flotte (OPS04, non fan-out par tenant) sont volontairement absents.
        var expected = new[]
        {
            "Liakont.Modules.Supervision.Infrastructure.SupervisionEvaluationTrigger",
            "Liakont.Modules.Archive.Infrastructure.DailyAnchoringTrigger",
            "Liakont.Modules.Pipeline.Contracts.Jobs.SendAllTrigger",
            "Liakont.Modules.Pipeline.Contracts.Jobs.SyncAllTrigger",
            "Liakont.Modules.Pipeline.Contracts.Jobs.AggregatePaymentsAllTrigger",
            "Liakont.Modules.Pipeline.Contracts.Jobs.RectifyReportsAllTrigger",
            "Liakont.Modules.Reconciliation.Infrastructure.ReconciliationFanOutJobPayload",
            "Liakont.Modules.SupportTrace.Infrastructure.SupportTracePurgeTrigger",
            "Liakont.Modules.Mandats.Infrastructure.TacitAcceptance.SelfBilledAcceptanceTacitTrigger",
            "Liakont.Modules.Signature.Infrastructure.Drain.SignatureWebhookDrainTrigger",
        };

        SystemJobDefinitions.All.Select(d => d.JobType)
            .Should().BeEquivalentTo(
                expected,
                "tout fan-out récurrent doit être déclaré pour entrer dans le diagnostic de démarrage");
    }
}
