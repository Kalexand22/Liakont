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

// RB6 P2 : page socle MODIFIÉE (« Créé le » de la section Audit migré vers <LiakontDate>) → entre dans le
// périmètre de test (CLAUDE.md règle 19).
public sealed class AdminDelegationFormTests : BunitContext
{
    private static readonly Guid DelegationId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public AdminDelegationFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.DelegationView);

        // Le formulaire injecte IAgentQueries (listes déroulantes du mode création).
        Services.AddScoped<IAgentQueries>(_ => new FakeAgentQueries());
        Services.AddScoped<IDelegationQueries>(_ => new FakeDelegationQueries(delegation:
            new DelegationDto
            {
                Id = DelegationId,
                DelegatorId = Guid.NewGuid(),
                DelegatorName = "Jean Dupont",
                DelegateId = Guid.NewGuid(),
                DelegateName = "Alice Martin",
                Scope = "Signature des bons de commande",
                ValidFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                ValidUntil = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            }));
    }

    [Fact]
    public void Audit_Section_CreatedAt_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminDelegationForm>(p => p.Add(c => c.Id, DelegationId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='delegation-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » = ÉVÉNEMENT → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
    }
}
