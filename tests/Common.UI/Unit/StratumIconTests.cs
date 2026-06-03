namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class StratumIconTests : BunitContext
{
    [Fact]
    public void ShouldRenderMaterialSymbolByDefault()
    {
        var cut = Render<StratumIcon>(parameters => parameters
            .Add(p => p.Name, "search"));

        var icon = cut.Find("[data-icon-library='material']");
        icon.TextContent.Should().Be("search");
        icon.ClassList.Should().Contain("stratum-icon--material");
    }

    [Fact]
    public void ShouldDetectBootstrapIconFromBiPrefix()
    {
        var cut = Render<StratumIcon>(parameters => parameters
            .Add(p => p.Name, "bi-plus-lg"));

        var icon = cut.Find("[data-icon-library='bootstrap']");
        icon.GetAttribute("data-icon-name").Should().Be("bi-plus-lg");
        icon.ClassList.Should().Contain("bi");
        icon.ClassList.Should().Contain("bi-plus-lg");
    }
}
