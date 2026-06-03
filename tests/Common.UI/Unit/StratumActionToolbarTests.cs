namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class StratumActionToolbarTests : BunitContext
{
    public StratumActionToolbarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldNotRenderWhenNoActionsOrGroups()
    {
        var cut = Render<StratumActionToolbar>();

        cut.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void ShouldRenderToolbarRoleWithAriaLabel()
    {
        var actions = new List<GridAction> { new("test", "Test") };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        var toolbar = cut.Find("[role='toolbar']");
        toolbar.GetAttribute("aria-label").Should().Be("Actions");
    }

    [Fact]
    public void ShouldRenderPrimaryActionAsPrimaryButton()
    {
        var actions = new List<GridAction>
        {
            new("create", "Nouveau", IsPrimary: true),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        var button = cut.Find("[data-testid='action-create']");
        button.ClassList.Should().Contain("stratum-action-toolbar__button--primary");
        button.TextContent.Should().Contain("Nouveau");
    }

    [Fact]
    public void ShouldRenderSecondaryActionAsSecondaryButton()
    {
        var actions = new List<GridAction>
        {
            new("export", "Export"),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        var button = cut.Find("[data-testid='action-export']");
        button.ClassList.Should().Contain("stratum-action-toolbar__button--secondary");
    }

    [Fact]
    public void ShouldRenderIconWhenProvided()
    {
        var actions = new List<GridAction>
        {
            new("create", "Nouveau", Icon: "bi-plus-lg"),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        var icon = cut.Find("[data-testid='action-create'] [data-icon-library='bootstrap']");
        icon.GetAttribute("data-icon-name").Should().Be("bi-plus-lg");
    }

    [Fact]
    public void ShouldDisableActionWhenRequiresSelectionAndNoneSelected()
    {
        var actions = new List<GridAction>
        {
            new("delete", "Supprimer", RequiresSelection: true),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions)
            .Add(t => t.SelectedCount, 0));

        var button = cut.Find("[data-testid='action-delete']");
        button.HasAttribute("disabled").Should().BeTrue();
        button.GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void ShouldEnableActionWhenRequiresSelectionAndSomeSelected()
    {
        var actions = new List<GridAction>
        {
            new("delete", "Supprimer", RequiresSelection: true),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions)
            .Add(t => t.SelectedCount, 3));

        var button = cut.Find("[data-testid='action-delete']");
        button.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task ShouldInvokeCallbackOnClick()
    {
        var invoked = false;
        var actions = new List<GridAction>
        {
            new("run", "Run", Callback: () =>
            {
                invoked = true;
                return Task.CompletedTask;
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        await cut.Find("[data-testid='action-run']").ClickAsync(new());

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldShowConfirmationDialogWhenRequired()
    {
        var invoked = false;
        var actions = new List<GridAction>
        {
            new(
                "delete",
                "Supprimer",
                RequiresConfirmation: true,
                Callback: () =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions));

        await cut.Find("[data-testid='action-delete']").ClickAsync(new());
        invoked.Should().BeFalse();

        cut.Find("[data-testid='stratum-dialog']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderActionGroupToggle()
    {
        var groups = new List<GridActionGroup>
        {
            new("Actions", "settings", new List<GridAction>
            {
                new("dup", "Dupliquer"),
                new("status", "Changer statut"),
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.ActionGroups, groups));

        var toggle = cut.Find("[data-testid='action-group-0']");
        toggle.TextContent.Should().Contain("Actions");
        toggle.GetAttribute("aria-haspopup").Should().Be("true");
        toggle.GetAttribute("aria-expanded").Should().Be("false");

        cut.FindAll("[role='menuitem']").Should().BeEmpty();

        toggle.Click();

        toggle.GetAttribute("aria-expanded").Should().Be("true");
        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(2);
        items[0].TextContent.Should().Contain("Dupliquer");
        items[1].TextContent.Should().Contain("Changer statut");
    }

    [Fact]
    public void ShouldRenderDropdownMenuItemsWithMenuRole()
    {
        var groups = new List<GridActionGroup>
        {
            new("Menu", null, new List<GridAction>
            {
                new("a", "Action A"),
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.ActionGroups, groups));

        cut.Find("[data-testid='action-group-0']").Click();

        cut.Find("ul[role='menu']").Should().NotBeNull();
        cut.Find("[role='menuitem']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldCloseDropdownWhenToggleClickedAgain()
    {
        var groups = new List<GridActionGroup>
        {
            new("Actions", null, new List<GridAction>
            {
                new("a", "Action A"),
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.ActionGroups, groups));

        var toggle = cut.Find("[data-testid='action-group-0']");

        toggle.Click();
        cut.FindAll("[role='menuitem']").Should().HaveCount(1);

        toggle.Click();
        cut.FindAll("[role='menuitem']").Should().BeEmpty();
    }

    [Fact]
    public void ShouldCloseDropdownOnBackdropClick()
    {
        var groups = new List<GridActionGroup>
        {
            new("Actions", null, new List<GridAction>
            {
                new("a", "Action A"),
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.ActionGroups, groups));

        cut.Find("[data-testid='action-group-0']").Click();
        cut.FindAll("[role='menuitem']").Should().HaveCount(1);

        cut.Find(".stratum-action-toolbar__backdrop").Click();
        cut.FindAll("[role='menuitem']").Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldCloseDropdownWhenActionClicked()
    {
        var invoked = false;
        var groups = new List<GridActionGroup>
        {
            new("Actions", null, new List<GridAction>
            {
                new("a", "Action A", Callback: () =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                }),
            }),
        };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.ActionGroups, groups));

        cut.Find("[data-testid='action-group-0']").Click();

        await cut.Find("[data-testid='action-a']").ClickAsync(new());

        invoked.Should().BeTrue();
        cut.FindAll("[role='menuitem']").Should().BeEmpty();
    }

    [Fact]
    public void ShouldSupportCustomAriaLabel()
    {
        var actions = new List<GridAction> { new("test", "Test") };

        var cut = Render<StratumActionToolbar>(p => p
            .Add(t => t.Actions, actions)
            .Add(t => t.AriaLabel, "Party actions"));

        var toolbar = cut.Find("[role='toolbar']");
        toolbar.GetAttribute("aria-label").Should().Be("Party actions");
    }
}
