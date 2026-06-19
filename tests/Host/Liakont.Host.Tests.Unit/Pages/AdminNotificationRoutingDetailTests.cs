namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (« Créé le » / « Modifié le » de la section Audit migrés vers <LiakontDate>)
// → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminNotificationRoutingDetailTests : BunitContext
{
    private static readonly Guid RuleId = Guid.Parse("c3333333-3333-3333-3333-333333333333");

    public AdminNotificationRoutingDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: NotificationPermissions.RoutingRuleView);
        Services.AddScoped<IRoutingRuleQueries>(_ => new FakeRoutingRuleQueries(rules:
        [
            new RoutingRuleDto
            {
                Id = RuleId,
                Code = "RR-001",
                Name = "Routage réservations",
                EntityType = "Reservation",
                ServiceCode = "voirie",
                RecipientType = "ServiceEmail",
                RecipientValue = "voirie@commune.local",
                ConditionsJson = "[]",
                Priority = 0,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            },
        ]));
        Services.AddScoped<IServiceDefinitionQueries>(_ => new FakeServiceDefinitionQueries());
        Services.AddScoped<IRoutingEngine>(_ => new FakeRoutingEngine());
    }

    [Fact]
    public void Audit_Section_Dates_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminNotificationRoutingDetail>(p => p.Add(c => c.Id, RuleId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='notif-routing-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
        cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
    }
}
