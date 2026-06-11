namespace Liakont.Modules.TenantSettings.Tests.Integration;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// <see cref="SetAlertContactEmailHandler"/> (FIX210) : met à jour le SEUL contact d'alerte du tenant (F12 §5.3)
/// sans altérer le reste du profil, journalise l'opération sans exposer l'adresse, et refuse l'opération si le
/// profil n'existe pas encore.
/// </summary>
[Collection("TenantSettingsIntegration")]
public sealed class SetAlertContactEmailIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public SetAlertContactEmailIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Updates_Only_The_Contact_Email_And_Keeps_Other_Fields()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        await SeedProfileAsync(harness, "ancien@exemple.test");

        var handler = new SetAlertContactEmailHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);
        await handler.Handle(new SetAlertContactEmailCommand { ContactEmailAlerte = "nouveau@exemple.test" }, CancellationToken.None);

        var dto = await harness.Queries.GetTenantProfile(harness.CompanyId);
        dto.Should().NotBeNull();
        dto!.ContactEmailAlerte.Should().Be("nouveau@exemple.test");

        // Le reste du profil est intact.
        dto.Siren.Should().Be("123456782");
        dto.RaisonSociale.Should().Be("Société Fictive");
        dto.Country.Should().Be("FR");
    }

    [Fact]
    public async Task Blank_Email_Clears_The_Contact()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        await SeedProfileAsync(harness, "alertes@exemple.test");

        var handler = new SetAlertContactEmailHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);
        await handler.Handle(new SetAlertContactEmailCommand { ContactEmailAlerte = "  " }, CancellationToken.None);

        var dto = await harness.Queries.GetTenantProfile(harness.CompanyId);
        dto!.ContactEmailAlerte.Should().BeNull();
    }

    [Fact]
    public async Task Journals_The_Update_As_An_Audited_Activity()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        await SeedProfileAsync(harness, contactEmail: null);

        var handler = new SetAlertContactEmailHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);
        await handler.Handle(new SetAlertContactEmailCommand { ContactEmailAlerte = "secret@exemple.test" }, CancellationToken.None);

        harness.ActivityLogger.Entries.Should().Contain(e =>
            e.EntityType == "TenantProfile"
            && e.ActivityType == "updated"
            && e.ActorId == harness.UserId.ToString());
    }

    [Fact]
    public async Task Without_A_Profile_The_Update_Is_Refused()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetAlertContactEmailHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        var act = () => handler.Handle(
            new SetAlertContactEmailCommand { ContactEmailAlerte = "alertes@exemple.test" },
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private static async Task SeedProfileAsync(TenantSettingsHarness harness, string? contactEmail)
    {
        var save = new SaveTenantProfileHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);
        await save.Handle(
            new SaveTenantProfileCommand
            {
                Siren = "123456782",
                RaisonSociale = "Société Fictive",
                Street = "1 rue de l'Exemple",
                PostalCode = "35000",
                City = "Rennes",
                Country = "FR",
                ContactEmailAlerte = contactEmail,
            },
            CancellationToken.None);
    }
}
