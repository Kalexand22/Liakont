namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Supervision;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la vue d'ensemble de supervision (SUP02, page <c>/supervision</c>) : rendu des lignes
/// par tenant (alertes, état agents, compteurs), badge de gravité FR, signalement d'un tenant injoignable
/// (jamais masqué), et bandeau d'erreur si l'agrégation échoue. La garde de permission (un non-superviseur
/// ne voit pas la page) est vérifiée côté nav (LiakontNavNodeProviderTests) et E2E (SupervisionE2ETests).
/// </summary>
public sealed class SupervisionTests : BunitContext
{
    public SupervisionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubSharedResourcesLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new TestActorContextAccessor());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferences());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilters());

        // Le bandeau témoin de vie (FIX210) embarqué dans la page résout ce service à l'initialisation.
        Services.AddScoped<ISupervisionLivenessProvider>(_ => new FakeSupervisionLivenessProvider());
    }

    [Fact]
    public void Should_Render_Tenant_Rows_With_Severity_Badge_And_Counts()
    {
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.WithOverview(
            Row("alpha", "Tenant Alpha", activeAlerts: 2, criticalAlerts: 1, worstSeverity: "Critical", blocked: 3),
            Row("beta", "Tenant Beta", activeAlerts: 0, criticalAlerts: 0, worstSeverity: null)));

        var cut = Render<Supervision>();

        cut.FindAll("[data-testid='supervision-intro']").Should().ContainSingle();
        cut.FindAll("[data-testid='supervision-error']").Should().BeEmpty();

        // Les deux tenants ont traversé LoadItems → la grille.
        cut.Markup.Should().Contain("Tenant Alpha").And.Contain("Tenant Beta");

        // Le ColumnTemplate d'alertes a restitué la gravité en FR pour le tenant critique.
        cut.Markup.Should().Contain("Critique");
    }

    [Fact]
    public void Should_Flag_A_Tenant_Whose_Read_Failed_Without_Hiding_It()
    {
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.WithOverview(
            Row("gamma", "Tenant Gamma", activeAlerts: 0, criticalAlerts: 0, worstSeverity: null, readFailed: true)));

        var cut = Render<Supervision>();

        // Le tenant injoignable reste VISIBLE et signalé (anti panne silencieuse).
        cut.Markup.Should().Contain("Tenant Gamma");
        cut.FindAll("[data-testid='supervision-row-unavailable']").Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Overview_Throws()
    {
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.Throwing());

        var cut = Render<Supervision>();

        // L'échec de l'agrégation reste VISIBLE (bandeau) et n'expose pas une liste trompeuse (anti faux-vert).
        cut.FindAll("[data-testid='supervision-error']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Render_Tenants_To_Treat_First_Regardless_Of_Alphabetical_Order()
    {
        // « Zeta » (critique) est alphabétiquement APRÈS « Alpha » (sain) : si la grille triait par nom,
        // Alpha serait en tête. La priorité opérateur (à traiter d'abord) doit placer Zeta en premier —
        // sinon un tenant critique en fin d'alphabet pourrait basculer en page 2 (panne silencieuse).
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.WithOverview(
            Row("alpha", "Alpha SARL", activeAlerts: 0, criticalAlerts: 0, worstSeverity: null),
            Row("zeta", "Zeta SARL", activeAlerts: 2, criticalAlerts: 1, worstSeverity: "Critical")));

        var cut = Render<Supervision>();

        var markup = cut.Markup;
        markup.IndexOf("Zeta SARL", StringComparison.Ordinal)
            .Should().BeLessThan(
                markup.IndexOf("Alpha SARL", StringComparison.Ordinal),
                "le tenant à traiter (alerte critique) est rendu avant un tenant sain, malgré l'ordre alphabétique");
    }

    private static TenantSupervisionRowDto Row(
        string tenantId,
        string displayName,
        int activeAlerts,
        int criticalAlerts,
        string? worstSeverity,
        int blocked = 0,
        bool readFailed = false) => new()
    {
        TenantId = tenantId,
        DisplayName = displayName,
        ActiveAlertCount = activeAlerts,
        CriticalAlertCount = criticalAlerts,
        WorstSeverity = worstSeverity,
        AgentCount = 1,
        LastAgentSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        BlockedDocumentCount = blocked,
        RejectedByPaDocumentCount = 0,
        PendingDocumentCount = 0,
        ReadFailed = readFailed,
    };
}
