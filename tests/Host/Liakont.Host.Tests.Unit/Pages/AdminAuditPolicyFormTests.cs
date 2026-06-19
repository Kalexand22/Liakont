namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;
using Stratum.Modules.Audit.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (« Créé le » / « Modifié le » de la section Audit migrés vers <LiakontDate>)
// → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminAuditPolicyFormTests : BunitContext
{
    private static readonly Guid PolicyId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public AdminAuditPolicyFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: AuditPermissions.AuditView);
        Services.AddScoped<IAuditQueries>(_ => new FakeAuditQueries(policies:
        [
            new AuditPolicyDto
            {
                Id = PolicyId,
                EntityType = "Product",
                ModuleSource = "Showcase",
                IsEnabled = true,
                TrackedFields = new List<string>(),
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 6, 11, 9, 15, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void Audit_Section_Dates_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        // Mode View (Id fourni, route hors /edit) → la section Audit (datée) est rendue, repliée par défaut.
        var cut = Render<AdminAuditPolicyForm>(p => p.Add(c => c.Id, PolicyId));

        // Déplier la section Audit (SectionCard collapsé ne rend pas son corps).
        cut.WaitForElement("[data-testid='audit-policy-form-audit'] .section-card__toggle-btn").Click();

        // RB6 : « Créé le » / « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC EXPLICITE en bUnit.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
        cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
    }
}
