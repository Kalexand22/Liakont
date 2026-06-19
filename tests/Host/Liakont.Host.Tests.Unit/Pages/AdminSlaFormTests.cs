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
public sealed class AdminSlaFormTests : BunitContext
{
    private static readonly Guid SlaId = Guid.Parse("a1111111-1111-1111-1111-111111111111");

    public AdminSlaFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: NotificationPermissions.SlaView);
        Services.AddScoped<IDeliverySlaQueries>(_ => new FakeDeliverySlaQueries(slas:
        [
            new DeliverySlaDto
            {
                Id = SlaId,
                Category = "transactional",
                MaxDelaySeconds = 300,
                EscalationAction = "email",
                EscalationRecipient = "admin@exemple.fr",
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void Audit_Section_Dates_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminSlaForm>(p => p.Add(c => c.Id, SlaId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='sla-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
        cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
    }
}
