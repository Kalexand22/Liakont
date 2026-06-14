namespace Liakont.Modules.TenantSettings.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

[Collection("TenantSettingsIntegration")]
public sealed class TenantProfileIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public TenantProfileIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static SaveTenantProfileCommand ValidCommand(string raisonSociale = "Société Fictive")
    {
        return new SaveTenantProfileCommand
        {
            Siren = "123456782",
            RaisonSociale = raisonSociale,
            Street = "1 rue de l'Exemple",
            PostalCode = "35000",
            City = "Rennes",
            Country = "FR",
            ContactEmailAlerte = "alertes@exemple.test",
        };
    }

    [Fact]
    public async Task Save_Then_Get_RoundTrips()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SaveTenantProfileHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal);

        var id = await handler.Handle(ValidCommand(), CancellationToken.None);

        var dto = await harness.Queries.GetTenantProfile(harness.CompanyId);
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(id);
        dto.Siren.Should().Be("123456782");
        dto.Statut.Should().Be("Actif");
        dto.Country.Should().Be("FR");

        harness.ActivityLogger.Entries.Should().ContainSingle(e =>
            e.EntityType == "TenantProfile" && e.ActorId == harness.UserId.ToString());
    }

    [Fact]
    public async Task Save_Twice_Updates_Same_Profile()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SaveTenantProfileHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal);

        var firstId = await handler.Handle(ValidCommand("Première raison"), CancellationToken.None);
        var secondId = await handler.Handle(ValidCommand("Raison mise à jour"), CancellationToken.None);

        secondId.Should().Be(firstId);
        var dto = await harness.Queries.GetTenantProfile(harness.CompanyId);
        dto!.RaisonSociale.Should().Be("Raison mise à jour");
        dto.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Save_With_Changed_Siren_On_Existing_Throws()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SaveTenantProfileHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal);
        await handler.Handle(ValidCommand(), CancellationToken.None);

        var changed = ValidCommand() with { Siren = "000000000" };
        var act = () => handler.Handle(changed, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>("le SIREN est la clé fonctionnelle immuable (INV-TENANTSETTINGS-001).");
    }

    [Fact]
    public async Task Profiles_Are_Isolated_By_Company()
    {
        var tenantA = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var tenantB = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handlerA = new SaveTenantProfileHandler(tenantA.UowFactory, tenantA.CompanyFilter, tenantA.ActorAccessor, tenantA.Queries, tenantA.Journal);
        var handlerB = new SaveTenantProfileHandler(tenantB.UowFactory, tenantB.CompanyFilter, tenantB.ActorAccessor, tenantB.Queries, tenantB.Journal);

        await handlerA.Handle(ValidCommand("Tenant A"), CancellationToken.None);
        await handlerB.Handle(ValidCommand("Tenant B"), CancellationToken.None);

        // Chaque tenant ne voit que SON profil ; aucun écrasement croisé.
        (await tenantA.Queries.GetTenantProfile(tenantA.CompanyId))!.RaisonSociale.Should().Be("Tenant A");
        (await tenantB.Queries.GetTenantProfile(tenantB.CompanyId))!.RaisonSociale.Should().Be("Tenant B");
        (await tenantA.Queries.GetTenantProfile(Guid.NewGuid())).Should().BeNull("aucune fuite vers un tenant inconnu.");
    }
}
