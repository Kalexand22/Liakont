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

// RB6 P2 : page socle MODIFIÉE (date du détail + heure des changements de champs migrées vers <LiakontDate>)
// → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminAuditDetailTests : BunitContext
{
    private static readonly Guid EntryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AdminAuditDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: AuditPermissions.AuditView);
        Services.AddScoped<IAuditQueries>(_ => new FakeAuditQueries(
            activity: new ActivityDto
            {
                Id = EntryId,
                EntityType = "Product",
                EntityId = "42",
                ActivityType = "updated",
                Description = "Modification",
                ActorId = "user-1",
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
            fieldChanges:
            [
                new FieldChangeDto
                {
                    Id = Guid.NewGuid(),
                    EntryId = EntryId,
                    EntityType = "Product",
                    EntityId = "42",
                    FieldName = "Price",
                    OldValue = "10",
                    NewValue = "12",
                    ActorId = "user-1",
                    OccurredAt = new DateTimeOffset(2026, 6, 11, 8, 30, 0, TimeSpan.Zero),
                },
            ]));
    }

    [Fact]
    public void Detail_Date_And_Change_Time_Render_Via_LiakontDate_With_Utc_Fallback()
    {
        var cut = Render<AdminAuditDetail>(p => p.Add(c => c.EntryId, EntryId));

        // RB6 : repli UTC EXPLICITE en bUnit (fuseau navigateur non résolu). Le format de la page est conservé
        // (yyyy-MM-dd HH:mm:ss pour la date du fait, HH:mm:ss pour l'heure d'un changement de champ).
        cut.Markup.Should().Contain("2026-06-11 08:00:00 UTC");
        cut.Markup.Should().Contain("08:30:00 UTC");
    }
}
