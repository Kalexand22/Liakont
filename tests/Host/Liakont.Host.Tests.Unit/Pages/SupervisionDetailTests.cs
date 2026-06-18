namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
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
/// Tests bUnit du détail de supervision d'un tenant (SUP02, page <c>/supervision/{tenantId}</c>) : rendu des
/// compteurs, de l'état des agents et des alertes, ACQUITTEMENT via la quick-action (journalisé avec
/// l'identité opérateur), tenant inconnu, et bandeau d'erreur. Acquitter ne touche aucune base ici : le faux
/// agrégateur enregistre l'appel — preuve que la page route bien (tenant, alerte, opérateur).
/// </summary>
public sealed class SupervisionDetailTests : BunitContext
{
    public SupervisionDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddBrowserTimeZoneStub();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubSharedResourcesLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new TestActorContextAccessor("Superviseur Test"));
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferences());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilters());

        // Le bandeau témoin de vie (FIX210) embarqué dans la page résout ce service à l'initialisation.
        Services.AddScoped<ISupervisionLivenessProvider>(_ => new FakeSupervisionLivenessProvider());
    }

    [Fact]
    public void Should_Render_Counts_Agents_And_Alerts()
    {
        var detail = Detail(Alert(Guid.NewGuid(), "Critical", active: true));
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.WithDetail(detail));

        var cut = Render<SupervisionDetail>(p => p.Add(c => c.TenantId, "alpha"));

        cut.Find("[data-testid='supervision-detail-title']").TextContent.Should().Contain("Tenant Alpha");
        cut.Find("[data-testid='supervision-detail-blocked']").TextContent.Should().Contain("3");
        cut.Find("[data-testid='supervision-detail-rejected']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='supervision-detail-pending']").TextContent.Should().Contain("5");
        cut.FindAll("[data-testid='supervision-detail-agent']").Should().ContainSingle();

        // L'alerte a traversé la grille → le ColumnTemplate de gravité (FR).
        cut.Markup.Should().Contain("Critique");
    }

    [Fact]
    public void Acknowledging_An_Active_Alert_Routes_Tenant_Alert_And_Operator()
    {
        var alertId = Guid.NewGuid();
        var fake = FakeSupervisionDashboardQueries.WithDetail(Detail(Alert(alertId, "Critical", active: true)));
        Services.AddScoped<ISupervisionDashboardQueries>(_ => fake);

        var cut = Render<SupervisionDetail>(p => p.Add(c => c.TenantId, "alpha"));

        cut.Find("[data-testid='quick-action-acknowledge']").Click();

        fake.Acknowledgements.Should().ContainSingle();
        fake.Acknowledgements[0].Should().Be(("alpha", alertId, "Superviseur Test"));
    }

    [Fact]
    public void Acknowledging_An_Alert_Shows_Error_Banner_When_Ack_Returns_False()
    {
        var alertId = Guid.NewGuid();
        var fake = FakeSupervisionDashboardQueries.WithDetailAckFailing(Detail(Alert(alertId, "Critical", active: true)));
        Services.AddScoped<ISupervisionDashboardQueries>(_ => fake);

        var cut = Render<SupervisionDetail>(p => p.Add(c => c.TenantId, "alpha"));

        cut.Find("[data-testid='quick-action-acknowledge']").Click();

        cut.FindAll("[data-testid='supervision-detail-ack-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='supervision-detail-error']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_NotFound_When_Tenant_Is_Unknown()
    {
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.WithDetail(null));

        var cut = Render<SupervisionDetail>(p => p.Add(c => c.TenantId, "ghost"));

        cut.FindAll("[data-testid='supervision-detail-notfound']").Should().ContainSingle();
        cut.FindAll("[data-testid='supervision-detail-title']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Detail_Throws()
    {
        Services.AddScoped<ISupervisionDashboardQueries>(_ => FakeSupervisionDashboardQueries.Throwing());

        var cut = Render<SupervisionDetail>(p => p.Add(c => c.TenantId, "alpha"));

        cut.FindAll("[data-testid='supervision-detail-error']").Should().ContainSingle();
    }

    private static TenantSupervisionDetailDto Detail(params AlertDto[] alerts) => new()
    {
        TenantId = "alpha",
        DisplayName = "Tenant Alpha",
        Agents =
        [
            new AgentStatusDto
            {
                Name = "AGENT-01",
                IsRevoked = false,
                LastSeenAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                LastAgentVersion = "1.2.3",
            },
        ],
        ActiveAlerts = alerts.Where(a => a.IsActive).ToList(),
        RecentAlerts = alerts,
        BlockedDocumentCount = 3,
        RejectedByPaDocumentCount = 1,
        PendingDocumentCount = 5,
    };

    private static AlertDto Alert(Guid id, string severity, bool active, bool acknowledged = false) => new()
    {
        Id = id,
        TenantId = "alpha",
        RuleKey = "agent.mute",
        Severity = severity,
        Detail = "L'agent du client ne répond plus depuis plus de 24 h.",
        TriggeredUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        ResolvedUtc = active ? null : new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero),
        AcknowledgedBy = acknowledged ? "Autre Opérateur" : null,
        AcknowledgedUtc = acknowledged ? new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero) : null,
    };
}
