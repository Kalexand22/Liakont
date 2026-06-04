namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Infrastructure;
using Xunit;

public sealed class TenantSettingsParsingTests
{
    [Fact]
    public void ParseOperationCategory_Null_Or_Empty_Returns_Null()
    {
        // null = décision en attente : jamais deviné (INV-TENANTSETTINGS-004).
        TenantSettingsParsing.ParseOperationCategory(null).Should().BeNull();
        TenantSettingsParsing.ParseOperationCategory("   ").Should().BeNull();
    }

    [Fact]
    public void ParseOperationCategory_Known_Value_Parses()
    {
        TenantSettingsParsing.ParseOperationCategory("Mixte").Should().Be(OperationCategory.Mixte);
        TenantSettingsParsing.ParseOperationCategory("livraisonbiens").Should().Be(OperationCategory.LivraisonBiens);
    }

    [Fact]
    public void ParseOperationCategory_Unknown_Value_Throws()
    {
        var act = () => TenantSettingsParsing.ParseOperationCategory("Inconnu");

        act.Should().Throw<ArgumentException>("une catégorie inconnue ne doit jamais être devinée (CLAUDE.md n°2).");
    }

    [Fact]
    public void ParseStatus_Unknown_Throws()
    {
        var act = () => TenantSettingsParsing.ParseStatus("Zombie");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseEnvironment_Known_Parses_CaseInsensitive()
    {
        TenantSettingsParsing.ParseEnvironment("production").Should().Be(PaEnvironment.Production);
    }

    [Fact]
    public void ParseEnvironment_Unknown_Throws()
    {
        var act = () => TenantSettingsParsing.ParseEnvironment("Sandbox");

        act.Should().Throw<ArgumentException>();
    }
}
