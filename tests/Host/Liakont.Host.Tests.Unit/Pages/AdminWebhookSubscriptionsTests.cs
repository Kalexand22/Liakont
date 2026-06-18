namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (colonnes « Créé le » / « Modifié le » migrées vers <LiakontDate>) → entre
// dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminWebhookSubscriptionsTests : BunitContext
{
    public AdminWebhookSubscriptionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();

        // « Créé le » (defaultVisible:true) + « Modifié le » (defaultVisible:false) forcées visibles toutes
        // deux pour exercer leurs templates migrés.
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("Name", "CreatedAt", "UpdatedAt"));
        Services.AddScoped<IWebhookQueries>(_ => new FakeWebhookQueries(subscriptions:
        [
            new WebhookSubscriptionDto
            {
                Id = Guid.NewGuid(),
                Name = "Webhook ERP",
                EventType = "ReservationCreated",
                TargetUrl = "https://erp.exemple.fr/hook",
                IsActive = true,
                CompanyId = AdminPageTestServices.DefaultCompanyId,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void CreatedAt_And_UpdatedAt_Columns_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminWebhookSubscriptions>();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC en bUnit. « Modifié le »
        // forcée visible par la préférence stub (appliquée via un callback async de la grille → WaitForAssertion).
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
            cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
        });
    }
}
