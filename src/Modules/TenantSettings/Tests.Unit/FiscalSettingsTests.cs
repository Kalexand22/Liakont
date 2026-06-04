namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

public sealed class FiscalSettingsTests
{
    [Fact]
    public void Create_With_All_Null_Is_Accepted()
    {
        // null = décision de l'expert-comptable en attente = suspension (INV-TENANTSETTINGS-004).
        var settings = FiscalSettings.Create(Guid.NewGuid(), null, null, null);

        settings.VatOnDebits.Should().BeNull();
        settings.OperationCategory.Should().BeNull();
        settings.ReportingFrequency.Should().BeNull();
    }

    [Fact]
    public void ReportingFrequency_Is_Stored_Opaque()
    {
        // L'énumération n'est pas figée (F12-A §3.3) : la valeur est stockée telle quelle, jamais interprétée.
        var settings = FiscalSettings.Create(Guid.NewGuid(), false, OperationCategory.Mixte, "  Décadaire  ");

        settings.ReportingFrequency.Should().Be("Décadaire", "la valeur est conservée opaque (juste normalisée par trim).");
        settings.OperationCategory.Should().Be(OperationCategory.Mixte);
        settings.VatOnDebits.Should().BeFalse();
    }

    [Fact]
    public void Update_Can_Reset_To_Null()
    {
        var settings = FiscalSettings.Create(Guid.NewGuid(), true, OperationCategory.LivraisonBiens, "Mensuelle");

        settings.Update(null, null, null);

        settings.VatOnDebits.Should().BeNull();
        settings.OperationCategory.Should().BeNull();
        settings.ReportingFrequency.Should().BeNull();
        settings.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Empty_ReportingFrequency_Normalizes_To_Null()
    {
        var settings = FiscalSettings.Create(Guid.NewGuid(), null, null, "   ");

        settings.ReportingFrequency.Should().BeNull();
    }
}
