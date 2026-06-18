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

// RB6 P2 : page socle MODIFIÉE (colonne « Dernière connexion » migrée vers <LiakontDate>) → entre dans le
// périmètre de test (CLAUDE.md règle 19).
public sealed class AdminUsersTests : BunitContext
{
    public AdminUsersTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.UserView);
        Services.AddScoped<IIdentityQueries>(_ => new FakeIdentityQueries(users:
        [
            new UserDto
            {
                Id = Guid.NewGuid(),
                Username = "jdupont",
                Email = "jean.dupont@exemple.fr",
                DisplayName = "Jean Dupont",
                IsActive = true,
                LastLoginAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                Roles = [],
            },
        ]));
    }

    [Fact]
    public void LastLoginAt_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminUsers>();

        // RB6 : la dernière connexion est un ÉVÉNEMENT → fuseau navigateur. En bUnit le fuseau n'est pas résolu
        // → repli UTC EXPLICITE. La colonne « LastLoginAt » est defaultVisible:true → rendue sans préférence.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("11/06/2026 08:00 UTC"));
    }
}
