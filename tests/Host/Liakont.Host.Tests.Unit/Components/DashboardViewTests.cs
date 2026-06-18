namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class DashboardViewTests : BunitContext
{
    public DashboardViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Le graphique « Année en cours » (Chart du design-system) rend via JS interop : un rendu
        // no-op suffit ici — la table accessible (sr-table) et le callback de clic restent testables.
        Services.AddScoped<IChartRenderer, StubChartRenderer>();
        Services.AddBrowserTimeZoneStub();
    }

    // Périodes FIXES (juin 2026) : les bornes viennent du SERVICE via le modèle — la vue ne calcule
    // jamais de date. Les hrefs attendus sont donc des LITTÉRAUX (aucun recalcul miroir de la prod,
    // aucune dépendance à l'horloge du test).
    private static DashboardCounterScope Scope(
        string key, string label, DateOnly from, DateOnly to, params DashboardStateCount[] counts) => new()
        {
            Key = key,
            Label = label,
            From = from,
            To = to,
            Counts = counts.Length > 0 ? counts : [new DashboardStateCount("Detected", 0)],
        };

    private static DashboardCounterScope CurrentMonthScope(params DashboardStateCount[] counts) =>
        Scope("current-month", "Mois en cours", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), counts);

    private static DashboardCounterScope PreviousMonthScope(params DashboardStateCount[] counts) =>
        Scope("previous-month", "Mois précédent", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), counts);

    private static DashboardCounterScope YearScope(params DashboardStateCount[] counts) =>
        Scope("current-year", "Année en cours", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), counts);

    private static DashboardViewModel BuildModel(
        DashboardCounterScope? currentMonth = null,
        DashboardCounterScope? previousMonth = null,
        DashboardCounterScope? currentYear = null,
        IReadOnlyList<AgentStatusLine>? agents = null,
        DashboardTvaStatus tvaStatus = DashboardTvaStatus.NotConfigured,
        string? tvaValidatedBy = null,
        DateOnly? tvaValidatedDate = null,
        string? reportingFrequency = null,
        bool profileConfigured = true) => new()
        {
            ProfileConfigured = profileConfigured,
            CurrentMonth = currentMonth ?? CurrentMonthScope(),
            PreviousMonth = previousMonth ?? PreviousMonthScope(),
            CurrentYear = currentYear ?? YearScope(),
            Agents = agents ?? [],
            TvaStatus = tvaStatus,
            TvaValidatedBy = tvaValidatedBy,
            TvaValidatedDate = tvaValidatedDate,
            ReportingFrequency = reportingFrequency,
        };

    [Fact]
    public void Should_Render_State_Counters_With_Value_And_French_Badge()
    {
        var model = BuildModel(currentMonth: CurrentMonthScope(
            new DashboardStateCount("Detected", 2),
            new DashboardStateCount("Issued", 7),
            new DashboardStateCount("Blocked", 1)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var issued = cut.Find("[data-testid='counter-current-month-Issued']");
        issued.TextContent.Should().Contain("7");
        issued.TextContent.Should().Contain("Émis");
        cut.Find("[data-testid='counter-current-month-Blocked']").TextContent.Should().Contain("Bloqué");
    }

    [Fact]
    public void Should_Render_Current_And_Previous_Month_Scopes_With_Their_Labels()
    {
        var model = BuildModel(
            currentMonth: CurrentMonthScope(new DashboardStateCount("Issued", 4)),
            previousMonth: PreviousMonthScope(new DashboardStateCount("Issued", 9)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='dashboard-scope-current-month']").TextContent.Should().Contain("Mois en cours");
        cut.Find("[data-testid='dashboard-scope-previous-month']").TextContent.Should().Contain("Mois précédent");
        cut.Find("[data-testid='counter-current-month-Issued']").TextContent.Should().Contain("4");
        cut.Find("[data-testid='counter-previous-month-Issued']").TextContent.Should().Contain("9");
    }

    [Fact]
    public void Should_Link_Each_Counter_To_The_Documents_List_Filtered_On_Its_Own_Period()
    {
        // Drill-down : la tuile ouvre /documents filtrée sur son état ET sur les bornes de SON
        // périmètre (paramètres d'URL restaurés par la page Documents — issue #33). Compteur et
        // liste partagent les mêmes bornes : la liste montre exactement ce qui est compté.
        var model = BuildModel(
            currentMonth: CurrentMonthScope(new DashboardStateCount("Issued", 7)),
            previousMonth: PreviousMonthScope(new DashboardStateCount("Blocked", 1)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var issued = cut.Find("[data-testid='counter-current-month-Issued']");
        issued.TagName.Should().Be("A");
        issued.GetAttribute("href").Should().Be("/documents?etat=Issued&du=2026-06-01&au=2026-06-30");
        cut.Find("[data-testid='counter-previous-month-Blocked']").GetAttribute("href")
            .Should().Be("/documents?etat=Blocked&du=2026-05-01&au=2026-05-31");
    }

    [Fact]
    public void Should_Hide_Zero_Counters_And_Say_When_A_Scope_Is_Empty()
    {
        var model = BuildModel(
            currentMonth: CurrentMonthScope(
                new DashboardStateCount("Detected", 0),
                new DashboardStateCount("Blocked", 3)),
            previousMonth: PreviousMonthScope(new DashboardStateCount("Issued", 0)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        // Mois courant : seules les tuiles non nulles sont rendues (lot 2 : 16 zéros noyaient les 3 bloqués).
        cut.FindAll("[data-testid='counter-current-month-Blocked']").Should().ContainSingle();
        cut.FindAll("[data-testid='counter-current-month-Detected']").Should().BeEmpty();

        // Mois précédent : tout à zéro → le périmètre le dit en toutes lettres.
        cut.Find("[data-testid='dashboard-scope-previous-month-empty']").TextContent.Should().Contain("Aucun document");
        cut.FindAll("[data-testid='counter-previous-month-Issued']").Should().BeEmpty();
    }

    [Fact]
    public void An_Agent_That_Never_Reported_Should_Not_Be_Shown_As_Active()
    {
        // « Actif » + « Dernier contact : jamais » était contradictoire (retour de recette lot 2).
        var model = BuildModel(agents: [new AgentStatusLine("Agent neuf", null, null, false)]);

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        var line = cut.Find("[data-testid='dashboard-agent-line']");
        line.TextContent.Should().Contain("Jamais connecté");
        line.TextContent.Should().NotContain("Actif");
        line.TextContent.Should().NotContain("Dernier contact");
    }

    [Fact]
    public void Should_Render_Year_Chart_With_French_State_Labels()
    {
        var model = BuildModel(currentYear: YearScope(
            new DashboardStateCount("Issued", 12),
            new DashboardStateCount("Blocked", 3)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        // La table accessible du Chart (WCAG) rend les données sans JS : libellés FR + valeurs.
        var chart = cut.Find("[data-testid='dashboard-year-chart']");
        chart.TextContent.Should().Contain("Émis");
        chart.TextContent.Should().Contain("12");
        chart.TextContent.Should().Contain("Bloqué");
        cut.Find("[data-testid='dashboard-year']").TextContent.Should().Contain("Année en cours");
    }

    [Fact]
    public async Task Clicking_A_Year_Chart_Bar_Should_Raise_The_Raw_State()
    {
        var model = BuildModel(currentYear: YearScope(
            new DashboardStateCount("Issued", 12),
            new DashboardStateCount("Blocked", 3)));
        string? selected = null;

        var cut = Render<DashboardView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.OnYearStateSelected, state => selected = state));

        // Le clic vient du JS (chart.js) via OnChartPointClick : on l'invoque directement, comme
        // le ferait l'interop. DataIndex 1 = deuxième point = état brut « Blocked ».
        var chart = cut.FindComponent<Chart<DashboardChartPoint>>();
        await cut.InvokeAsync(() => chart.Instance.OnChartPointClick("Documents", "Bloqué", 3, dataIndex: 1));

        selected.Should().Be("Blocked", "le callback remonte l'état BRUT (la page construit l'URL du périmètre année)");
    }

    [Fact]
    public async Task Year_Chart_Click_Index_Should_Map_To_The_Non_Zero_States_Only()
    {
        // Un zéro INTERCALÉ (Detected=0 entre Issued et Blocked) : le graphique ne rend que les états
        // non nuls, le DataIndex du clic indexe donc la liste FILTRÉE — pas les compteurs complets.
        var model = BuildModel(currentYear: YearScope(
            new DashboardStateCount("Issued", 12),
            new DashboardStateCount("Detected", 0),
            new DashboardStateCount("Blocked", 3)));
        string? selected = null;

        var cut = Render<DashboardView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.OnYearStateSelected, state => selected = state));

        var chart = cut.FindComponent<Chart<DashboardChartPoint>>();
        await cut.InvokeAsync(() => chart.Instance.OnChartPointClick("Documents", "Bloqué", 3, dataIndex: 1));

        selected.Should().Be("Blocked", "l'index 1 du graphique est le 2e état NON NUL (Detected à zéro est masqué)");
    }

    [Fact]
    public void Year_Chart_Should_Be_Replaced_By_A_Message_When_The_Year_Is_Empty()
    {
        var model = BuildModel(currentYear: YearScope(new DashboardStateCount("Issued", 0)));

        var cut = Render<DashboardView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='dashboard-year-chart']").Should().BeEmpty();
        cut.Find("[data-testid='dashboard-year-empty']").TextContent.Should().Contain("Aucun document");
    }

    [Fact]
    public async Task Clicking_A_Year_Chart_Bar_Out_Of_Range_Should_Be_Ignored()
    {
        var model = BuildModel(currentYear: YearScope(new DashboardStateCount("Issued", 12)));
        string? selected = null;

        var cut = Render<DashboardView>(p => p
            .Add(v => v.Model, model)
            .Add(v => v.OnYearStateSelected, state => selected = state));

        var chart = cut.FindComponent<Chart<DashboardChartPoint>>();
        await cut.InvokeAsync(() => chart.Instance.OnChartPointClick("Documents", "?", 0, dataIndex: 99));

        selected.Should().BeNull("un index hors du périmètre (données périmées côté JS) ne déclenche rien");
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
            new AgentStatusLine("Agent A", new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero), "1.2.3", false),
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

    private sealed class StubChartRenderer : IChartRenderer
    {
        public Task InitializeAsync(IJSObjectReference jsModule, string containerId, ChartConfig config) => Task.CompletedTask;

        public Task UpdateAsync(IJSObjectReference jsModule, string containerId, ChartConfig config) => Task.CompletedTask;

        public Task DisposeAsync(IJSObjectReference jsModule, string containerId) => Task.CompletedTask;
    }
}
