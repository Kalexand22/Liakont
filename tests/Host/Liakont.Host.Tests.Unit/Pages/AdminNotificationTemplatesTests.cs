namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (colonne « Dernière modif. » migrée vers <LiakontDate>) → entre dans le
// périmètre de test (CLAUDE.md règle 19).
public sealed class AdminNotificationTemplatesTests : BunitContext
{
    public AdminNotificationTemplatesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: NotificationPermissions.View);

        // Force « Créé le » (CreatedAt, defaultVisible:false) visible pour exercer aussi son template migré.
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("UpdatedAt", "CreatedAt"));
        Services.AddScoped<IEmailTemplateQueries>(_ => new FakeEmailTemplateQueries(templates:
        [
            new EmailTemplateDto
            {
                Id = Guid.NewGuid(),
                Code = "welcome",
                SubjectTemplate = "Bienvenue",
                BodyTemplate = "Corps",
                LanguageCode = "fr",
                Category = "transactional",
                EntityType = "User",
                CreatedAt = new DateTimeOffset(2026, 6, 11, 7, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void UpdatedAt_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminNotificationTemplates>();

        // RB6 : « Dernière modif. » (UpdatedAt=08:00) ET « Créé le » (CreatedAt=07:00, forcée visible) = ÉVÉNEMENTS
        // → fuseau navigateur, repli UTC EXPLICITE en bUnit. Les deux templates migrés sont exercés.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
            cut.Markup.Should().Contain("11/06/2026 07:00 UTC");
        });
    }
}
