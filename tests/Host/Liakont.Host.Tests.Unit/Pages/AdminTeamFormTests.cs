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

// RB6 P2 : page socle MODIFIÉE (« Créé le » / « Modifié le » de la section Audit + « Depuis le » des membres
// migrés vers <LiakontDate>) → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminTeamFormTests : BunitContext
{
    private static readonly Guid TeamId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public AdminTeamFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.TeamView);
        Services.AddScoped<ITeamQueries>(_ => new FakeTeamQueries(
            team: new TeamDto
            {
                Id = TeamId,
                Code = "TECH",
                Name = "Équipe technique",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            },
            members:
            [
                new TeamMemberDto
                {
                    Id = Guid.NewGuid(),
                    TeamId = TeamId,
                    UserId = Guid.NewGuid(),
                    Username = "amartin",
                    DisplayName = "Alice Martin",
                    JoinedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                },
            ]));
    }

    [Fact]
    public void Audit_Dates_And_Member_JoinedAt_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la table des membres est rendue (non collapsée) et la section
        // Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminTeamForm>(p => p.Add(c => c.Id, TeamId));

        // « Depuis le » d'un membre = DateOnly → format « dd/MM/yyyy » suffixé UTC. La table membres n'est pas
        // collapsée en mode View.
        cut.Markup.Should().Contain("11/06/2026 UTC");

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='team-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
        cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
    }
}
