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

// RB6 P2 : page socle MODIFIÉE (onglet « Clés API », colonne « Créée le » migrée vers <LiakontDate> ;
// « Expire le » = date de VALIDITÉ LAISSÉE en valeur brute volontairement) → entre dans le périmètre de
// test (CLAUDE.md règle 19).
public sealed class AdminIntegrationsTests : BunitContext
{
    public AdminIntegrationsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // IntegrationView est obligatoire : OnInitializedAsync redirige vers « / » si absent.
        Services.AddAdminPageStubs(permissions: NotificationPermissions.IntegrationView);

        // La page consomme IHttpClientFactory (test de connexion) — non fourni par AddAdminPageStubs.
        Services.AddHttpClient();
        Services.AddScoped<IApiKeyQueries>(_ => new FakeApiKeyQueries(keys:
        [
            new ApiKeyDto
            {
                Id = Guid.NewGuid(),
                Name = "Mobile App",
                KeyPrefix = "lk_abcd",
                Scopes = ["*"],
                RateLimit = 1000,
                IsRevoked = false,
                CompanyId = AdminPageTestServices.DefaultCompanyId,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                ExpiresAt = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            },
        ]));
        Services.AddScoped<IIntegrationConfigQueries>(_ => new FakeIntegrationConfigQueries());
    }

    [Fact]
    public void CreatedAt_Migrated_But_ExpiresAt_Left_As_Raw_Date()
    {
        // L'onglet « Clés API » est le premier onglet (SelectedIndex défaut = 0) → rendu immédiatement.
        var cut = Render<AdminIntegrations>();

        cut.WaitForAssertion(() =>
        {
            // RB6 : « Créée le » (CreatedAt) = ÉVÉNEMENT → migré → repli UTC en bUnit (8h UTC seedé).
            cut.Markup.Should().Contain("11/06/2026 08:00 UTC");

            // « Expire le » (ExpiresAt) = date de VALIDITÉ/échéance → NON migrée : rendue brute, donc SANS
            // suffixe UTC (preuve discriminante que la migration ne l'a pas touchée).
            cut.Markup.Should().NotContain("31/12/2026 UTC");
            cut.Markup.Should().NotContain("31/12/2026 00:00 UTC");
        });
    }
}
