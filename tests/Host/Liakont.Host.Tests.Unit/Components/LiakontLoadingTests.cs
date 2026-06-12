namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

public sealed class LiakontLoadingTests : BunitContext
{
    [Fact]
    public void Should_Render_Default_French_Label_With_Status_Role()
    {
        var cut = Render<LiakontLoading>(p => p.Add(l => l.TestId, "test-loading"));

        var loading = cut.Find("[data-testid='test-loading']");
        loading.TextContent.Should().Contain("Chargement…");
        loading.GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void Should_Render_Custom_Label()
    {
        var cut = Render<LiakontLoading>(p => p.Add(l => l.Label, "Vérification en cours…"));

        cut.Find(".liakont-loading").TextContent.Should().Contain("Vérification en cours…");
    }

    [Fact]
    public void Should_Hide_Spinner_From_Screen_Readers()
    {
        // Le wrapper role="status" annonce déjà le libellé : le spinner (role="progressbar" du
        // CircularProgress) doit être masqué pour éviter une double annonce.
        var cut = Render<LiakontLoading>();

        var spinner = cut.Find(".liakont-loading__spinner");
        spinner.GetAttribute("aria-hidden").Should().Be("true");
        spinner.QuerySelectorAll("svg").Should().ContainSingle("le spinner du design-system est rendu");
    }
}
