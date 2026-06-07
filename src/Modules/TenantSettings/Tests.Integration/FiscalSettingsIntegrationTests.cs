namespace Liakont.Modules.TenantSettings.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

[Collection("TenantSettingsIntegration")]
public sealed class FiscalSettingsIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public FiscalSettingsIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Null_Fiscal_Parameters_Persist_As_Null()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetFiscalSettingsHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        // Tous null = décision en attente = suspension (INV-TENANTSETTINGS-004), jamais de défaut.
        await handler.Handle(
            new SetFiscalSettingsCommand { VatOnDebits = null, OperationCategory = null, ReportingFrequency = null },
            CancellationToken.None);

        var dto = await harness.Queries.GetFiscalSettings(harness.CompanyId);
        dto.Should().NotBeNull();
        dto!.VatOnDebits.Should().BeNull();
        dto.OperationCategory.Should().BeNull();
        dto.ReportingFrequency.Should().BeNull();
        dto.FeeImputationMethod.Should().BeNull();
    }

    [Fact]
    public async Task Set_Then_Update_Persists_Values()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetFiscalSettingsHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetFiscalSettingsCommand { VatOnDebits = false, OperationCategory = "Mixte", ReportingFrequency = "Décadaire", FeeImputationMethod = "AgregationJourTaux" },
            CancellationToken.None);
        await handler.Handle(
            new SetFiscalSettingsCommand { VatOnDebits = true, OperationCategory = null, ReportingFrequency = null, FeeImputationMethod = null },
            CancellationToken.None);

        var dto = await harness.Queries.GetFiscalSettings(harness.CompanyId);
        dto!.VatOnDebits.Should().BeTrue();
        dto.OperationCategory.Should().BeNull();
        dto.ReportingFrequency.Should().BeNull();
        dto.FeeImputationMethod.Should().BeNull("null = méthode d'imputation non décidée (jamais de prorata par défaut).");
        dto.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FeeImputationMethod_Round_Trips()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetFiscalSettingsHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetFiscalSettingsCommand { VatOnDebits = false, OperationCategory = "PrestationServices", ReportingFrequency = "Mensuelle", FeeImputationMethod = "Prorata" },
            CancellationToken.None);

        var dto = await harness.Queries.GetFiscalSettings(harness.CompanyId);
        dto!.FeeImputationMethod.Should().Be("Prorata");
    }

    [Fact]
    public async Task Unknown_FeeImputationMethod_Is_Rejected()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetFiscalSettingsHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        var act = () => handler.Handle(
            new SetFiscalSettingsCommand { FeeImputationMethod = "Lettrage" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>("une méthode inconnue n'est jamais devinée (CLAUDE.md n°2).");
    }

    [Fact]
    public async Task Unknown_OperationCategory_Is_Rejected()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetFiscalSettingsHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        var act = () => handler.Handle(
            new SetFiscalSettingsCommand { OperationCategory = "Inconnu" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>("une catégorie inconnue n'est jamais devinée (CLAUDE.md n°2).");
    }
}
