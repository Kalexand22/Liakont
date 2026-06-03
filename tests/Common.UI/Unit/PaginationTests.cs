namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class PaginationTests : BunitContext
{
    public PaginationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldHideWhenSinglePageByDefault()
    {
        var cut = Render<Pagination>(parameters => parameters
            .Add(component => component.TotalItems, 6)
            .Add(component => component.PageSize, 25)
            .Add(component => component.CurrentPage, 1));

        cut.FindAll("[data-testid='pagination']").Should().BeEmpty();
    }

    [Fact]
    public void ShouldRenderWhenMultiplePagesExist()
    {
        var cut = Render<Pagination>(parameters => parameters
            .Add(component => component.TotalItems, 51)
            .Add(component => component.PageSize, 25)
            .Add(component => component.CurrentPage, 1));

        cut.Find("[data-testid='pagination']").Should().NotBeNull();
        cut.FindAll(".pagination__btn").Should().HaveCount(4);
    }

    [Fact]
    public void ShouldRenderWhenHideWhenSinglePageIsDisabled()
    {
        var cut = Render<Pagination>(parameters => parameters
            .Add(component => component.TotalItems, 6)
            .Add(component => component.PageSize, 25)
            .Add(component => component.HideWhenSinglePage, false));

        cut.Find("[data-testid='pagination']").Should().NotBeNull();
    }

    [Fact]
    public void HiddenSinglePageShouldStillApplyStoredPageSizePreference()
    {
        var pageSize = 25;
        JSInterop.Setup<int>("stratumUI.getGridPageSize").SetResult(50);

        Render<Pagination>(parameters => parameters
            .Add(component => component.TotalItems, 6)
            .Add(component => component.PageSize, pageSize)
            .Add(component => component.OnPageSizeChanged, EventCallback.Factory.Create<int>(this, size => pageSize = size)));

        pageSize.Should().Be(50);
    }
}
