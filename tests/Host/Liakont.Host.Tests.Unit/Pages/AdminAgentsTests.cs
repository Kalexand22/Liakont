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
public sealed class AdminAgentsTests : BunitContext
{
    public AdminAgentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.AgentView);

        // « Créé le » (defaultVisible:false) forcée visible pour exercer son template migré.
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("DisplayName", "CreatedAt"));
        Services.AddScoped<IAgentQueries>(_ => new FakeAgentQueries(agents:
        [
            new AgentDto
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Username = "amartin",
                Email = "alice.martin@exemple.fr",
                DisplayName = "Alice Martin",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void CreatedAt_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminAgents>();

        // RB6 : « Créé le » = ÉVÉNEMENT → fuseau navigateur, repli UTC en bUnit. Colonne forcée visible par la
        // préférence stub (appliquée via un callback async de la grille → WaitForAssertion).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("11/06/2026 08:00 UTC"));
    }
}
