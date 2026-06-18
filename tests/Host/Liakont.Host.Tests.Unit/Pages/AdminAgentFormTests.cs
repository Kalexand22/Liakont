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

// RB6 P2 : page socle MODIFIÉE (« Créé le » / « Modifié le » de la section Audit migrés vers <LiakontDate>) →
// entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminAgentFormTests : BunitContext
{
    private static readonly Guid AgentId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public AdminAgentFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.AgentView);
        Services.AddScoped<IAgentQueries>(_ => new FakeAgentQueries(agent:
            new AgentDto
            {
                Id = AgentId,
                UserId = Guid.NewGuid(),
                Username = "amartin",
                Email = "alice.martin@exemple.fr",
                DisplayName = "Alice Martin",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            }));
    }

    [Fact]
    public void Audit_Section_Dates_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminAgentForm>(p => p.Add(c => c.Id, AgentId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='agent-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
        cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
    }
}
