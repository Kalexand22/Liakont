namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ShortcutService"/>.
/// Covers scope-stack priority resolution, binding computation, command execution,
/// and visible-command grouping.
/// </summary>
public sealed class ShortcutServiceTests
{
    // ── ScopeBinding.KeyId contract ───────────────────────────────────────
    [Theory]
    [InlineData("?", false, false, false, "?")]
    [InlineData("!", false, false, false, "!")]
    [InlineData("s", true, false, false, "ctrl+s")]
    [InlineData("s", false, false, true, "shift+s")]
    [InlineData("Enter", false, false, false, "enter")]
    [InlineData("Escape", false, false, false, "escape")]
    public void ScopeBindingKeyIdShouldMatchExpected(
        string key,
        bool ctrl,
        bool alt,
        bool shift,
        string expectedKeyId)
    {
        var binding = new ScopeBinding("cmd", key, ctrl: ctrl, alt: alt, shift: shift);
        binding.KeyId.Should().Be(
            expectedKeyId,
            "C# KeyId must match the JS _keyId() output for the same inputs");
    }

    // ── ComputeActiveBindings ─────────────────────────────────────────────
    [Fact]
    public void ComputeActiveBindingsShouldReturnEmptyWhenNoScopesRegistered()
    {
        var svc = CreateService();
        svc.ComputeActiveBindings().Should().BeEmpty();
    }

    [Fact]
    public void ComputeActiveBindingsShouldExcludeBindingsWithoutHandler()
    {
        var svc = CreateService();
        svc.PushScope(
            "p",
            ShortcutScopeType.Page,
            [new ScopeBinding("open-search", "/")]);

        svc.ComputeActiveBindings().Should().BeEmpty("bindings without a handler are not active");
    }

    [Fact]
    public void ComputeActiveBindingsShouldIncludeBindingsWithHandler()
    {
        var svc = CreateService();
        svc.PushScope(
            "p",
            ShortcutScopeType.Page,
            [new ScopeBinding("save", "s", ctrl: true, handler: () => Task.CompletedTask)]);

        svc.ComputeActiveBindings().Should().ContainKey("ctrl+s")
            .WhoseValue.Should().Be("save");
    }

    [Fact]
    public void ComputeActiveBindingsShouldLetHigherScopeTypeOverwriteLowerForSameKey()
    {
        var svc = CreateService();
        svc.PushScope(
            "page",
            ShortcutScopeType.Page,
            [new ScopeBinding("page-save", "s", ctrl: true, handler: () => Task.CompletedTask)]);
        svc.PushScope(
            "widget",
            ShortcutScopeType.Widget,
            [new ScopeBinding("widget-save", "s", ctrl: true, handler: () => Task.CompletedTask)]);

        var bindings = svc.ComputeActiveBindings();
        bindings["ctrl+s"].Should().Be("widget-save", "Widget (priority 2) overrides Page (priority 1)");
    }

    // ── ExecuteCommandAsync ────────────────────────────────────────────────
    [Fact]
    public async Task ExecuteCommandAsyncShouldInvokeHandlerInHighestPriorityScope()
    {
        var svc = CreateService();
        var log = new List<string>();

        svc.PushScope(
            "page",
            ShortcutScopeType.Page,
            [
                new ScopeBinding(
                    "save",
                    "s",
                    ctrl: true,
                    handler: () =>
                    {
                        log.Add("page");
                        return Task.CompletedTask;
                    }),
            ]);
        svc.PushScope(
            "widget",
            ShortcutScopeType.Widget,
            [
                new ScopeBinding(
                    "save",
                    "s",
                    ctrl: true,
                    handler: () =>
                    {
                        log.Add("widget");
                        return Task.CompletedTask;
                    }),
            ]);

        await svc.ExecuteCommandAsync("save");

        log.Should().ContainSingle().Which.Should().Be(
            "widget",
            "Widget scope (priority 2) takes precedence over Page scope (priority 1)");
    }

    [Fact]
    public async Task ExecuteCommandAsyncShouldFallThroughToLowerScopeWhenHigherHasNoHandler()
    {
        var svc = CreateService();
        var log = new List<string>();

        svc.PushScope(
            "page",
            ShortcutScopeType.Page,
            [
                new ScopeBinding(
                    "save",
                    "s",
                    ctrl: true,
                    handler: () =>
                    {
                        log.Add("page");
                        return Task.CompletedTask;
                    }),
            ]);
        svc.PushScope(
            "widget",
            ShortcutScopeType.Widget,
            [new ScopeBinding("other", "x")]);

        await svc.ExecuteCommandAsync("save");

        log.Should().ContainSingle().Which.Should().Be(
            "page",
            "Widget has no handler for 'save', so Page handler is invoked");
    }

    [Fact]
    public async Task ExecuteCommandAsyncShouldDoNothingWhenCommandNotRegistered()
    {
        var svc = CreateService();
        var invoked = false;

        svc.PushScope(
            "page",
            ShortcutScopeType.Page,
            [
                new ScopeBinding(
                    "save",
                    "s",
                    ctrl: true,
                    handler: () =>
                    {
                        invoked = true;
                        return Task.CompletedTask;
                    }),
            ]);

        await svc.ExecuteCommandAsync("nonexistent");

        invoked.Should().BeFalse();
    }

    // ── Scope push / pop ──────────────────────────────────────────────────
    [Fact]
    public void PopScopeShouldRemoveScopeAndRaiseScopeChangedEvent()
    {
        var svc = CreateService();
        var changed = 0;
        svc.ScopeChanged += () => changed++;

        svc.PushScope("p", ShortcutScopeType.Page, []);
        svc.PushScope("w", ShortcutScopeType.Widget, []);
        svc.PopScope("p");

        changed.Should().Be(3, "two pushes + one pop");

        // After removing page scope, only widget scope should remain
        svc.PushScope(
            "check",
            ShortcutScopeType.Modal,
            [new ScopeBinding("cmd", "x", handler: () => Task.CompletedTask)]);
        svc.ComputeActiveBindings().Should().ContainKey("x");
    }

    [Fact]
    public void PopScopeShouldDoNothingWhenScopeIdNotFound()
    {
        var svc = CreateService();
        var changed = 0;
        svc.ScopeChanged += () => changed++;

        svc.PushScope("p", ShortcutScopeType.Page, []);
        var before = changed;
        svc.PopScope("nonexistent");

        changed.Should().Be(before, "no event raised when scope ID not found");
    }

    // ── GetVisibleCommands ────────────────────────────────────────────────
    [Fact]
    public void GetVisibleCommandsShouldGroupCommandsByScopeType()
    {
        var svc = CreateService();
        svc.Register(new CommandDefinition("save", "Enregistrer", ShortcutScopeType.Page, "Ctrl+S"));
        svc.Register(new CommandDefinition("open-help", "Aide", ShortcutScopeType.Global, "?"));

        svc.PushScope(
            "global",
            ShortcutScopeType.Global,
            [new ScopeBinding("open-help", "?", handler: () => Task.CompletedTask)]);
        svc.PushScope(
            "page",
            ShortcutScopeType.Page,
            [new ScopeBinding("save", "s", ctrl: true, handler: () => Task.CompletedTask)]);

        var groups = svc.GetVisibleCommands(svc);

        groups.Should().HaveCount(2);
        groups[0].ScopeType.Should().Be(ShortcutScopeType.Global);
        groups[1].ScopeType.Should().Be(ShortcutScopeType.Page);
    }

    [Fact]
    public void GetVisibleCommandsShouldNotIncludeDuplicatesWithinSameGroup()
    {
        var svc = CreateService();
        svc.Register(new CommandDefinition("save", "Enregistrer", ShortcutScopeType.Page, "Ctrl+S"));

        svc.PushScope(
            "page1",
            ShortcutScopeType.Page,
            [new ScopeBinding("save", "s", ctrl: true, handler: () => Task.CompletedTask)]);
        svc.PushScope(
            "page2",
            ShortcutScopeType.Page,
            [new ScopeBinding("save", "s", ctrl: true, handler: () => Task.CompletedTask)]);

        var groups = svc.GetVisibleCommands(svc);
        var pageGroup = groups.Single(g => g.ScopeType == ShortcutScopeType.Page);
        pageGroup.Commands.Should().HaveCount(1, "duplicate 'save' commands in same scope group are deduplicated");
    }

    // ── ICommandRegistry ──────────────────────────────────────────────────
    [Fact]
    public void RegisterShouldReplaceExistingCommandAndRaiseChangedEvent()
    {
        var svc = CreateService();
        var changed = 0;
        svc.Changed += () => changed++;

        svc.Register(new CommandDefinition("save", "Save v1", ShortcutScopeType.Page));
        svc.Register(new CommandDefinition("save", "Save v2", ShortcutScopeType.Page));

        changed.Should().Be(2);
        svc.GetAll().Should().ContainSingle(c => c.Id == "save")
            .Which.Label.Should().Be("Save v2");
    }

    [Fact]
    public void UnregisterShouldRemoveCommandAndRaiseChangedEvent()
    {
        var svc = CreateService();
        var changed = 0;
        svc.Changed += () => changed++;

        svc.Register(new CommandDefinition("save", "Save", ShortcutScopeType.Page));
        svc.Unregister("save");

        changed.Should().Be(2);
        svc.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void UnregisterShouldNotRaiseEventWhenCommandNotFound()
    {
        var svc = CreateService();
        var changed = 0;
        svc.Changed += () => changed++;

        svc.Unregister("nonexistent");

        changed.Should().Be(0);
    }

    private static ShortcutService CreateService() => new();
}
