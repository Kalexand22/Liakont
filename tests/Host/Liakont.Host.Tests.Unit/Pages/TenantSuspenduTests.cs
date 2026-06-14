namespace Liakont.Host.Tests.Unit.Pages;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Xunit;

/// <summary>
/// Page anonyme « espace suspendu » (OPS03.4 lot B) : message opérateur français explicite
/// (données conservées + action corrective) et lien de reconnexion — jamais une 500 ni une
/// boucle de login.
/// </summary>
public sealed class TenantSuspenduTests : BunitContext
{
    [Fact]
    public void Should_Explain_The_Suspension_In_French_With_A_Retry_Link()
    {
        var cut = Render<TenantSuspendu>();

        var message = cut.Find("[data-testid='tenant-suspendu-message']").TextContent;
        message.Should().Contain("suspendu", "le motif est explicite");
        message.Should().Contain("conservées", "l'opérateur sait que ses données restent intactes");
        cut.Find("[data-testid='tenant-suspendu']").TextContent.Should().Contain("opérateur Liakont", "l'action corrective est donnée");

        cut.Find("[data-testid='tenant-suspendu-retry']").GetAttribute("href").Should().Be("/");
    }
}
