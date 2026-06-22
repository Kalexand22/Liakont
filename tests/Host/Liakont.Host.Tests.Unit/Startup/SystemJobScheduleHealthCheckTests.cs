namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Host.Startup;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;
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
    public void All_FanOut_Handlers_With_ITenantJobRunner_Are_Declared_In_SystemJobDefinitions()
    {
        // Jobs opt-in VOLONTAIREMENT absents de SystemJobDefinitions :
        // • SUP03 digest : planification opt-in, défaut désactivé — un warning de démarrage serait du bruit.
        var allowlistedPayloadNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Liakont.Modules.Supervision.Infrastructure.SupervisionDigestTrigger",
        };

        // Force-load des assemblies référencées par le Host pour s'assurer qu'elles sont dans l'AppDomain.
        foreach (AssemblyName name in typeof(Liakont.Host.Startup.AppBootstrap).Assembly.GetReferencedAssemblies())
        {
            try
            {
                Assembly.Load(name);
            }
            catch
            {
                // Ignore les assemblies système non chargées ou introuvables.
            }
        }

        var fanOutPayloadNames = new List<string>();
        Type jobHandlerOpenGeneric = typeof(IJobHandler<>);
        Type tenantJobRunnerType = typeof(ITenantJobRunner);

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Liakont.", StringComparison.Ordinal) == true))
        {
            IEnumerable<Type> types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null)!;
            }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !type.IsClass)
                {
                    continue;
                }

                // Un handler de fan-out = implémente IJobHandler<TPayload> et a un constructeur injectant
                // ITenantJobRunner (signature des handlers qui font le fan-out sur tous les tenants).
                Type? handlerInterface = type.GetInterfaces()
                    .FirstOrDefault(i =>
                        i.IsGenericType
                        && i.GetGenericTypeDefinition() == jobHandlerOpenGeneric);

                if (handlerInterface is null)
                {
                    continue;
                }

                bool injectsTenantRunner = type.GetConstructors()
                    .Any(c => c.GetParameters()
                        .Any(p => p.ParameterType == tenantJobRunnerType));

                if (!injectsTenantRunner)
                {
                    continue;
                }

                Type payloadType = handlerInterface.GetGenericArguments()[0];
                if (payloadType.FullName is { } fullName)
                {
                    fanOutPayloadNames.Add(fullName);
                }
            }
        }

        // Sanité : le scan doit avoir trouvé au moins un handler (sinon le test passe à vide).
        fanOutPayloadNames.Should().NotBeEmpty(
            "le scan par réflexion doit trouver au moins un handler de fan-out (ITenantJobRunner) — "
            + "si ce n'est pas le cas, vérifier que les assemblies Liakont.* sont bien chargées");

        var declared = new HashSet<string>(
            SystemJobDefinitions.All.Select(d => d.JobType),
            StringComparer.Ordinal);

        foreach (string payloadName in fanOutPayloadNames.Where(n => !allowlistedPayloadNames.Contains(n)))
        {
            string reason = $"{payloadName} est un handler de fan-out (injecte ITenantJobRunner) mais n'est pas"
                + " déclaré dans SystemJobDefinitions → il ne sera pas signalé au démarrage s'il n'est jamais"
                + " planifié (job mort)";
            declared.Should().Contain(payloadName, reason);
        }
    }
}
