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

// RB6 P2 : page socle MODIFIÉE (colonne « Modifié le » de l'onglet Configuration migrée vers <LiakontDate>)
// → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminSlaTests : BunitContext
{
    public AdminSlaTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: NotificationPermissions.SlaView);

        // L'onglet « Configuration » est l'onglet par défaut : sa grille (StratumDataGrid avec une colonne
        // explicite « Modifié le » via <StratumColumn><Template>) est rendue immédiatement, sans clic d'onglet.
        Services.AddScoped<IDeliverySlaQueries>(_ => new FakeDeliverySlaQueries(slas:
        [
            new DeliverySlaDto
            {
                Id = Guid.NewGuid(),
                Category = "transactional",
                MaxDelaySeconds = 300,
                EscalationAction = "email",
                EscalationRecipient = "admin@exemple.fr",
                CreatedAt = new DateTimeOffset(2026, 6, 11, 7, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
        ]));

        // Onglet Monitoring : 1 breach pour exercer la migration « Envoyé le » (SentAt). 10:30 ≠ 08:00 (config)
        // → discriminant : la chaîne n'existe que dans la carte de breach.
        Services.AddScoped<IDeliveryRecordQueries>(_ => new FakeDeliveryRecordQueries(breaches:
        [
            new DeliveryRecordDto
            {
                Id = Guid.NewGuid(),
                TemplateCode = "sla-breach",
                RecipientEmail = "ops@exemple.fr",
                SentAt = new DateTimeOffset(2026, 6, 11, 10, 30, 0, TimeSpan.Zero),
                RetryCount = 0,
                SlaBreached = true,
            },
        ]));
    }

    [Fact]
    public void Config_Modified_And_Monitoring_SentAt_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminSla>();

        // Onglet Configuration (défaut) : « Modifié le » = ÉVÉNEMENT → fuseau navigateur, repli UTC (8h UTC seedé).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("11/06/2026 08:00 UTC"));

        // Onglet Monitoring : « Envoyé le » (SentAt) d'un breach = ÉVÉNEMENT → migré (10:30 UTC seedé).
        cut.Find("[data-testid='sla-tab-monitoring']").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("11/06/2026 10:30 UTC"));
    }
}
