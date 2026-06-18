namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;
using Stratum.Modules.Audit.Web.Pages;
using Xunit;

// RB6 P2 : page socle MODIFIÉE (colonne « Date » du journal migrée vers <LiakontDate>) → entre dans le
// périmètre de test (CLAUDE.md règle 19).
public sealed class AdminAuditTests : BunitContext
{
    public AdminAuditTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: AuditPermissions.AuditView);
        Services.AddScoped<IAuditQueries>(_ => new FakeAuditQueries(entries:
        [
            new AuditSearchResultDto
            {
                Id = Guid.NewGuid(),
                EntityType = "Product",
                EntityId = "42",
                ActivityType = "created",
                Description = "Création produit",
                ActorId = "user-1",
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
                ChangeCount = 0,
            },
        ]));
    }

    [Fact]
    public void Date_Column_Renders_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminAudit>();

        // RB6 : la date d'audit est un ÉVÉNEMENT → fuseau navigateur. En bUnit la sonde du shell est absente
        // (fuseau non résolu) → repli UTC EXPLICITE. L'ancien rendu était le DateTimeOffset serveur brut.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
    }
}
