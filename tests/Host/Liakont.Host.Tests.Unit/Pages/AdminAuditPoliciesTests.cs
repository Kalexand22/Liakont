namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;
using Stratum.Modules.Audit.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (colonnes « Créé le » / « Modifié le » migrées vers <LiakontDate>) → entre
// dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminAuditPoliciesTests : BunitContext
{
    public AdminAuditPoliciesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: AuditPermissions.AuditView);

        // Force « Modifié le » (defaultVisible:false) visible pour exercer aussi son template migré.
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("EntityType", "CreatedAt", "UpdatedAt"));
        Services.AddScoped<IAuditQueries>(_ => new FakeAuditQueries(policies:
        [
            new AuditPolicyDto
            {
                Id = Guid.NewGuid(),
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
    public void Created_Date_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminAuditPolicies>();

        // RB6 : « Créé le » + « Modifié le » = ÉVÉNEMENTS → fuseau navigateur, repli UTC en bUnit.
        // « Modifié le » forcée visible par la préférence stub (appliquée via un callback async de la grille →
        // WaitForAssertion) → son template migré est bien exercé.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
            cut.Markup.Should().Contain("11/06/2026 09:15 UTC");
        });
    }
}
