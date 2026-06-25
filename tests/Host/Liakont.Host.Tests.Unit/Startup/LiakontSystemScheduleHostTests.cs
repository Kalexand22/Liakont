namespace Liakont.Host.Tests.Unit.Startup;

using System;
using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

/// <summary>
/// BUG-4b : la société porteuse système résout TOUS les jobs de fan-out (source unique
/// <see cref="SystemJobDefinitions"/>) vers la même société porteuse plateforme, laisse les jobs
/// tenant-scopés sans porteuse, et expose un sentinel qui n'est PAS un tenant réel.
/// </summary>
public sealed class LiakontSystemScheduleHostTests
{
    private readonly LiakontSystemScheduleHost _host = new();

    [Fact]
    public void Every_System_Job_Type_Resolves_To_The_Host_Company()
    {
        SystemJobDefinitions.All.Should().NotBeEmpty();
        foreach (var def in SystemJobDefinitions.All)
        {
            _host.ResolveHostCompanyId(def.JobType).Should().Be(
                LiakontSystemScheduleHost.HostCompanyId,
                "« {0} » est un job SYSTÈME (fan-out tous tenants), porté par la société porteuse",
                def.Label);
        }
    }

    [Fact]
    public void Tenant_Scoped_Or_Unknown_Type_Has_No_Host_Company()
    {
        _host.ResolveHostCompanyId("Some.Unknown.Mono.Tenant.JobType").Should().BeNull();
        _host.ResolveHostCompanyId(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Cross_Tenant_List_Targets_The_Host_Company()
    {
        _host.CrossTenantHostCompanyId.Should().Be(LiakontSystemScheduleHost.HostCompanyId);
    }

    [Fact]
    public void Host_Company_Is_A_Platform_Sentinel_Not_A_Real_Tenant()
    {
        // Sentinel non vide et DISTINCT du tenant `default` (…a000-0001, verrouillé par
        // DefaultCompanyIdCoherenceTests) : il ne désigne aucune société de tenant.
        LiakontSystemScheduleHost.HostCompanyId.Should().NotBe(Guid.Empty);
        LiakontSystemScheduleHost.HostCompanyId.Should()
            .NotBe(Guid.Parse("00000000-0000-4000-a000-000000000001"));
    }
}
