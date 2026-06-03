namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class StratumDialogTests : BunitContext
{
    public StratumDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldNotRenderWhenNotVisible()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, false)
            .Add(d => d.Title, "Test Dialog"));

        cut.FindAll(".stratum-dialog").Count.Should().Be(0);
        cut.FindAll(".stratum-dialog-backdrop").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderWhenVisible()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test Dialog"));

        cut.Find(".stratum-dialog").Should().NotBeNull();
        cut.Find(".stratum-dialog-backdrop").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderTitleInHeader()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Confirmer"));

        cut.Find(".stratum-dialog__title").TextContent.Should().Contain("Confirmer");
    }

    [Fact]
    public void ShouldHaveDialogRoleAndAriaModal()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        var dialog = cut.Find(".stratum-dialog");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
    }

    [Fact]
    public void ShouldHaveAriaLabelledByPointingToTitle()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        var dialog = cut.Find(".stratum-dialog");
        var titleId = cut.Find(".stratum-dialog__title").GetAttribute("id");

        dialog.GetAttribute("aria-labelledby").Should().Be(titleId);
    }

    [Fact]
    public void ShouldRenderBodyContent()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.ChildContent, BuildContent("body-text", "Dialog body")));

        cut.Find(".body-text").TextContent.Should().Be("Dialog body");
    }

    [Fact]
    public void ShouldRenderActionsInFooter()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.Actions, BuildContent("action-btn", "OK")));

        cut.Find(".stratum-dialog__footer").Should().NotBeNull();
        cut.Find(".action-btn").TextContent.Should().Be("OK");
    }

    [Fact]
    public void ShouldNotRenderFooterWhenNoActions()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        cut.FindAll(".stratum-dialog__footer").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderCloseButtonByDefault()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        cut.Find(".stratum-dialog__close").Should().NotBeNull();
        cut.Find(".stratum-dialog__close").GetAttribute("aria-label")
            .Should().Be("Fermer");
    }

    [Fact]
    public void ShouldNotRenderCloseButtonWhenDisabled()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.ShowCloseButton, false));

        cut.FindAll(".stratum-dialog__close").Count.Should().Be(0);
    }

    [Fact]
    public async Task EscShouldCloseDialogWhenCloseOnEscIsTrue()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.CloseOnEsc, true)
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        await cut.Find(".stratum-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        visible.Should().BeFalse();
    }

    [Fact]
    public async Task EscShouldNotCloseDialogWhenCloseOnEscIsFalse()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.CloseOnEsc, false)
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        await cut.Find(".stratum-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        visible.Should().BeTrue();
    }

    [Fact]
    public async Task OverlayClickShouldCloseDialogWhenCloseOnOverlayIsTrue()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.CloseOnOverlay, true)
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        await cut.Find(".stratum-dialog-backdrop").ClickAsync(new MouseEventArgs());

        visible.Should().BeFalse();
    }

    [Fact]
    public async Task OverlayClickShouldNotCloseDialogWhenCloseOnOverlayIsFalse()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.CloseOnOverlay, false)
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        await cut.Find(".stratum-dialog-backdrop").ClickAsync(new MouseEventArgs());

        visible.Should().BeTrue();
    }

    [Fact]
    public async Task CloseButtonShouldCloseDialog()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        await cut.Find(".stratum-dialog__close").ClickAsync(new MouseEventArgs());

        visible.Should().BeFalse();
    }

    [Fact]
    public async Task CloseShouldFireOnCloseCallback()
    {
        var closeFired = false;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.OnClose, EventCallback.Factory.Create(this, () => closeFired = true)));

        await cut.Find(".stratum-dialog__close").ClickAsync(new MouseEventArgs());

        closeFired.Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyDefaultWidth()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        cut.Find(".stratum-dialog").GetAttribute("style").Should().Contain("500px");
    }

    [Fact]
    public void ShouldApplyCustomWidth()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.Width, "800px"));

        cut.Find(".stratum-dialog").GetAttribute("style").Should().Contain("800px");
    }

    [Fact]
    public void ShouldFallbackToDefaultWidthOnInvalidInput()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.Width, "javascript:alert(1)"));

        cut.Find(".stratum-dialog").GetAttribute("style").Should().Contain("500px");
    }

    [Fact]
    public void ShouldAcceptPercentWidth()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.Width, "80%"));

        cut.Find(".stratum-dialog").GetAttribute("style").Should().Contain("80%");
    }

    [Fact]
    public void ShouldAcceptRemWidth()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.Width, "30rem"));

        cut.Find(".stratum-dialog").GetAttribute("style").Should().Contain("30rem");
    }

    [Fact]
    public void BackdropShouldHaveAriaHidden()
    {
        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test"));

        cut.Find(".stratum-dialog-backdrop")
            .GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public async Task NonEscKeysShouldNotCloseDialog()
    {
        var visible = true;

        var cut = Render<StratumDialog>(p => p
            .Add(d => d.Visible, true)
            .Add(d => d.Title, "Test")
            .Add(d => d.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visible = v)));

        var dialog = cut.Find(".stratum-dialog");
        await dialog.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await dialog.KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        visible.Should().BeTrue();
    }

    private static RenderFragment BuildContent(string cssClass, string text)
    {
        return builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", cssClass);
            builder.AddContent(2, text);
            builder.CloseElement();
        };
    }
}
