namespace Liakont.Modules.TenantSettings.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Activation du vertical enchères (lot FIX03, décision opérateur D4) : défaut OFF quand aucune ligne
/// n'existe, round-trip de l'upsert tenant-scopé, journalisation de la mutation (piste d'audit
/// append-only — CLAUDE.md n°4).
/// </summary>
[Collection("TenantSettingsIntegration")]
public sealed class AuctionVerticalSettingsIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public AuctionVerticalSettingsIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Absent_Defaults_To_Off()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());

        // Aucune commande envoyée : une ligne absente vaut « vertical enchères OFF » (défaut produit D4).
        var enabled = await harness.Queries.GetAuctionVerticalEnabled(harness.CompanyId);

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_Then_Disable_Round_Trips_And_Journals()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetAuctionVerticalActivationHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(new SetAuctionVerticalActivationCommand { Enabled = true }, CancellationToken.None);
        (await harness.Queries.GetAuctionVerticalEnabled(harness.CompanyId)).Should().BeTrue();

        await handler.Handle(new SetAuctionVerticalActivationCommand { Enabled = false }, CancellationToken.None);
        (await harness.Queries.GetAuctionVerticalEnabled(harness.CompanyId)).Should().BeFalse();

        // La mutation est tracée dans la piste d'audit (création puis mise à jour).
        harness.ActivityLogger.Entries.Should()
            .OnlyContain(a => a.EntityType == "AuctionVerticalSettings" && a.CompanyId == harness.CompanyId);
        harness.ActivityLogger.Entries.Select(a => a.ActivityType).Should().Equal("created", "updated");
    }

    [Fact]
    public async Task Activation_Is_Tenant_Scoped()
    {
        var tenantA = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var tenantB = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handlerA = new SetAuctionVerticalActivationHandler(tenantA.UowFactory, tenantA.CompanyFilter, tenantA.Journal);

        await handlerA.Handle(new SetAuctionVerticalActivationCommand { Enabled = true }, CancellationToken.None);

        (await tenantA.Queries.GetAuctionVerticalEnabled(tenantA.CompanyId)).Should().BeTrue();
        (await tenantB.Queries.GetAuctionVerticalEnabled(tenantB.CompanyId)).Should().BeFalse("l'activation d'un tenant n'affecte pas un autre");
    }
}
