namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

/// <summary>
/// Agrégat d'activation du vertical enchères (lot FIX03, décision opérateur D4) : défaut produit OFF
/// (jamais une activation implicite — blueprint §2 règle 7) et bascule explicite.
/// </summary>
public sealed class AuctionVerticalSettingsTests
{
    [Fact]
    public void CreateDefault_Is_Off()
    {
        var settings = AuctionVerticalSettings.CreateDefault(Guid.NewGuid());

        settings.Enabled.Should().BeFalse("le vertical enchères est désactivé par défaut (D4)");
        settings.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_Persists_Requested_State(bool enabled)
    {
        var settings = AuctionVerticalSettings.Create(Guid.NewGuid(), enabled);

        settings.Enabled.Should().Be(enabled);
    }

    [Fact]
    public void Update_Toggles_And_Stamps_UpdatedAt()
    {
        var settings = AuctionVerticalSettings.CreateDefault(Guid.NewGuid());

        settings.Update(enabled: true);

        settings.Enabled.Should().BeTrue();
        settings.UpdatedAt.Should().NotBeNull();
    }
}
