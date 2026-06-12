namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Dashboard;
using Xunit;

public sealed class DashboardViewTests : BunitContext
{
    public DashboardViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static DashboardViewModel BuildModel(
        IReadOnlyList<DashboardStateCount>? counts = null,
        IReadOnlyList<DashboardAgentLine>? agents = null,
        DashboardTvaStatus tvaStatus = DashboardTvaStatus.NotConfigured,
        string? tvaValidatedBy = null,
        DateOnly? tvaValidatedDate = null,
        string? reportingFrequency = null,
        bool profileConfigured = true) => new()
        {
            ProfileConfigured = profileConfigured,
            StateCounts = counts ?? [new DashboardStateCount("Detected", 0)],
            Agents = agents ?? [],
            TvaStatus = tvaStatus,
            TvaValidatedBy = tvaValidatedBy,
            TvaValidatedDate = tvaValidatedDate,
            ReportingFrequency = reportingFrequency,
        };

    [Fact]
    public void Should_Render_State_Counters_With_Value_And_French_Badge()
    {
        var model = BuildModel(counts:
        [
            new DashboardStateCount("Detected", 2),
            new DashboardStateCount("Issued", 7),
            new DashboardStateCount("Blocked", 1),
        ]);

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var issued = cut.Find("[data-testid='counter-Issued']");
        issued.TextContent.Should().Contain("7");
        issued.TextContent.Should().Contain("Émis");
        cut.Find("[data-testid='counter-Blocked']").TextContent.Should().Contain("Bloqué");
    }

    [Fact]
    public void Should_Link_Each_Counter_To_The_Filtered_Documents_List()
    {
        // Drill-down : la tuile ouvre /documents filtrée sur son état via le paramètre d'URL
        // « etat » (restauré par la page Documents — issue #33), même geste que les pastilles
        // du DocumentCountsBanner.
        var model = BuildModel(counts:
        [
            new DashboardStateCount("Issued", 7),
            new DashboardStateCount("Blocked", 1),
        ]);

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var issued = cut.Find("[data-testid='counter-Issued']");
        issued.TagName.Should().Be("A");
        issued.GetAttribute("href").Should().Be("/documents?etat=Issued");
        cut.Find("[data-testid='counter-Blocked']").GetAttribute("href").Should().Be("/documents?etat=Blocked");
    }

    [Fact]
    public void Should_Show_Empty_Agent_Message_When_No_Agent()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(agents: [])));

        cut.FindAll("[data-testid='dashboard-agent-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='dashboard-agent-line']").Should().BeEmpty();
    }

    [Fact]
    public void Should_List_Agents_With_Heartbeat_And_Version()
    {
        var model = BuildModel(agents:
        [
            new DashboardAgentLine("Agent A", new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero), "1.2.3", false),
        ]);

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var line = cut.Find("[data-testid='dashboard-agent-line']");
        line.TextContent.Should().Contain("Agent A");
        line.TextContent.Should().Contain("1.2.3");
        line.TextContent.Should().Contain("Actif");
    }

    [Fact]
    public void Should_Show_Profile_Incomplete_Banner_When_Profile_Not_Configured()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(profileConfigured: false)));

        var banner = cut.Find("[data-testid='dashboard-profile-incomplete']");
        banner.TextContent.Should().Contain("PARAMÉTRAGE INCOMPLET");
        banner.TextContent.Should().Contain("suspendu");
    }

    [Fact]
    public void Should_Not_Show_Profile_Incomplete_Banner_When_Profile_Configured()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(profileConfigured: true)));

        cut.FindAll("[data-testid='dashboard-profile-incomplete']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Tva_Not_Validated_Alert()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(tvaStatus: DashboardTvaStatus.NotValidated)));

        var alert = cut.Find("[data-testid='dashboard-tva-alert']");
        alert.TextContent.Should().Contain("NON VALIDÉE");
        alert.TextContent.Should().Contain("suspendus");
    }

    [Fact]
    public void Should_Show_Tva_Validated_With_Validator()
    {
        var model = BuildModel(
            tvaStatus: DashboardTvaStatus.Validated,
            tvaValidatedBy: "Cabinet Comptable",
            tvaValidatedDate: new DateOnly(2026, 6, 1));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='dashboard-tva-validated']").TextContent.Should().Contain("Cabinet Comptable");
        cut.FindAll("[data-testid='dashboard-tva-alert']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Missing_Frequency_Banner_When_Null()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(reportingFrequency: null)));

        cut.Find("[data-testid='dashboard-frequency-missing']").TextContent
            .Should().Contain("Fréquence déclarative non renseignée");
        cut.FindAll("[data-testid='dashboard-frequency']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Fiscal_Link_In_Missing_Frequency_Banner()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(reportingFrequency: null)));

        var link = cut.Find("[data-testid='dashboard-frequency-missing-link']");
        link.GetAttribute("href").Should().Be("/parametrage/fiscal");
    }

    [Fact]
    public void Should_Show_Declared_Frequency_Without_Computing_A_Deadline()
    {
        var cut = Render<DashboardView>(p => p.Add(v => v.Model, BuildModel(reportingFrequency: "Mensuelle")));

        cut.Find("[data-testid='dashboard-frequency']").TextContent.Should().Contain("Mensuelle");
        cut.FindAll("[data-testid='dashboard-frequency-missing']").Should().BeEmpty();
    }
}
