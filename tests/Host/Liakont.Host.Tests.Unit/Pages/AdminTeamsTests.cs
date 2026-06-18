namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (colonne « Créé le » migrée vers <LiakontDate>) → entre dans le périmètre de
// test (CLAUDE.md règle 19).
public sealed class AdminTeamsTests : BunitContext
{
    public AdminTeamsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.TeamView);

        // « Créé le » (defaultVisible:false) forcée visible pour exercer son template migré.
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("Code", "CreatedAt"));
        Services.AddScoped<ITeamQueries>(_ => new FakeTeamQueries(teams:
        [
            new TeamDto
            {
                Id = Guid.NewGuid(),
                Code = "TECH",
                Name = "Équipe technique",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void CreatedAt_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminTeams>();

        // RB6 : « Créé le » = ÉVÉNEMENT → fuseau navigateur, repli UTC en bUnit. Colonne forcée visible par la
        // préférence stub (appliquée via un callback async de la grille → WaitForAssertion).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("11/06/2026 08:00 UTC"));
    }
}
