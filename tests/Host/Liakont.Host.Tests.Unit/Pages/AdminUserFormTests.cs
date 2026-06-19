namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (« Dernière connexion » de la section Audit migrée vers <LiakontDate>) → entre
// dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminUserFormTests : BunitContext
{
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public AdminUserFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.UserView);
        Services.AddScoped<IIdentityQueries>(_ => new FakeIdentityQueries(user:
            new UserDto
            {
                Id = UserId,
                Username = "jdupont",
                Email = "jean.dupont@exemple.fr",
                DisplayName = "Jean Dupont",
                ExternalId = "kc-subject-1",
                IsActive = true,
                LastLoginAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                Roles = [],
            }));
    }

    [Fact]
    public void Audit_Section_LastLoginAt_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminUserForm>(p => p.Add(c => c.Id, UserId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='user-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Dernière connexion » = ÉVÉNEMENT → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
    }
}
