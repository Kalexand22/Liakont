namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (« Créé le » de la section Audit migré vers <LiakontDate>) → entre dans le
// périmètre de test (CLAUDE.md règle 19).
public sealed class AdminRoleFormTests : BunitContext
{
    private static readonly Guid RoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public AdminRoleFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.RolesManage);
        Services.AddScoped<IPermissionCatalog>(_ => new FakePermissionCatalog());
        Services.AddScoped<IIdentityQueries>(_ => new FakeIdentityQueries(role:
            new RoleDetailDto
            {
                Id = RoleId,
                Name = "Agent terrain",
                Description = "Rôle personnalisé",
                IsSystem = false,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                GrantedPermissions = [],
            }));
    }

    [Fact]
    public void Audit_Section_CreatedAt_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminRoleForm>(p => p.Add(c => c.Id, RoleId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='role-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » = ÉVÉNEMENT → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
    }
}
