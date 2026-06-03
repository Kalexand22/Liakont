namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class ShortcutHelpDialogTests : BunitContext
{
    public ShortcutHelpDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var svc = new ShortcutService();
        Services.AddSingleton<IShortcutService>(svc);
        Services.AddSingleton<ICommandRegistry>(svc);
    }

    [Fact]
    public void ShouldNotRenderWhenNotOpen()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, false));

        cut.FindAll(".shortcut-dialog").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderWhenOpen()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true));

        cut.Find(".shortcut-dialog").Should().NotBeNull();
    }

    [Fact]
    public void ShouldHaveDialogRoleAndAriaModal()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true));

        var dialog = cut.Find(".shortcut-dialog");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
    }

    [Fact]
    public void ShouldHaveInstanceUniqueAriaLabelledBy()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true));

        var dialog = cut.Find(".shortcut-dialog");
        var titleId = cut.Find(".shortcut-dialog__title").GetAttribute("id");

        titleId.Should().NotBeNull();
        dialog.GetAttribute("aria-labelledby").Should().Be(titleId);

        // Instance-unique: must not be the static "shortcut-dialog-title"
        titleId.Should().NotBe("shortcut-dialog-title");
    }

    [Fact]
    public void ShouldCallSaveFocusAndRegisterFocusTrapOnOpen()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true));

        JSInterop.Invocations.Should().Contain(
            i => i.Identifier == "stratumUI.saveFocus",
            "saveFocus should be called when dialog opens");

        JSInterop.Invocations.Should().Contain(
            i => i.Identifier == "stratumUI.registerDialogFocusTrap",
            "registerDialogFocusTrap should be called when dialog opens");
    }

    [Fact]
    public async Task EscShouldCloseDialog()
    {
        var closeFired = false;

        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.OnClose, EventCallback.Factory.Create(this, () => closeFired = true)));

        await cut.Find(".shortcut-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        closeFired.Should().BeTrue();
    }

    [Fact]
    public void BackdropShouldHaveAriaHidden()
    {
        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true));

        cut.Find(".shortcut-dialog-backdrop")
            .GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public async Task BackdropClickShouldCloseDialog()
    {
        var closeFired = false;

        var cut = Render<ShortcutHelpDialog>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.OnClose, EventCallback.Factory.Create(this, () => closeFired = true)));

        await cut.Find(".shortcut-dialog-backdrop").ClickAsync(new MouseEventArgs());

        closeFired.Should().BeTrue();
    }
}
