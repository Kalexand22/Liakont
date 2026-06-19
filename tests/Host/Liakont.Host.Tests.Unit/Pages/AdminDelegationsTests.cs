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

// RB6 P2 : page socle MODIFIÉE (colonne « Créé le » migrée vers <LiakontDate> ; « Début »/« Fin » de validité
// LAISSÉES en date brute volontairement) → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminDelegationsTests : BunitContext
{
    public AdminDelegationsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs(permissions: IdentityPermissions.DelegationView);

        // « Créé le » (defaultVisible:false) forcée visible ; « Début » (ValidFrom) laissée pour la preuve
        // discriminante qu'une date de VALIDITÉ n'est PAS migrée (pas de suffixe UTC).
        Services.AddScoped<IGridPreferenceService>(_ => new FakeGridPreferenceService("DelegatorName", "ValidFrom", "CreatedAt"));
        Services.AddScoped<IDelegationQueries>(_ => new FakeDelegationQueries(delegations:
        [
            new DelegationDto
            {
                Id = Guid.NewGuid(),
                DelegatorId = Guid.NewGuid(),
                DelegatorName = "Jean Dupont",
                DelegateId = Guid.NewGuid(),
                DelegateName = "Alice Martin",
                Scope = "Signature des bons de commande",
                ValidFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                ValidUntil = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
            },
        ]));
    }

    [Fact]
    public void CreatedAt_Migrated_But_ValidFrom_Left_As_Raw_Date()
    {
        var cut = Render<AdminDelegations>();

        cut.WaitForAssertion(() =>
        {
            // RB6 : « Créé le » = ÉVÉNEMENT → migré → repli UTC en bUnit.
            cut.Markup.Should().Contain("11/06/2026 08:00 UTC");

            // « Début » (ValidFrom) = date de VALIDITÉ → NON migrée : rendue brute, donc SANS suffixe UTC.
            cut.Markup.Should().Contain("01/06/2026");
            cut.Markup.Should().NotContain("01/06/2026 UTC");
        });
    }
}
