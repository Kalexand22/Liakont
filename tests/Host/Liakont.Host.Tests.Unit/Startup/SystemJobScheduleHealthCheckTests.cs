namespace Liakont.Host.Tests.Unit.Startup;

using System;
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
        // Garde anti-régression : les vrais jobs système (supervision, ancrage) sont évalués.
        var missing = SystemJobScheduleHealthCheck.FindMissing(SystemJobDefinitions.All, Array.Empty<string>());

        missing.Should().HaveCount(SystemJobDefinitions.All.Count);
    }
}
