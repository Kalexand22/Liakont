namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class FilterBarTests : BunitContext
{
    public FilterBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IShortcutService, StubShortcutService>();
    }

    [Fact]
    public void ShouldRenderSearchShellAndShortcut()
    {
        var cut = Render<FilterBar>();

        cut.Find(".filter-bar__search-shell").Should().NotBeNull();
        cut.Find(".filter-bar__search-input").Should().NotBeNull();
        cut.Find(".filter-bar__shortcut").TextContent.Should().Be("/");
    }

    [Fact]
    public void ShouldWrapCustomFilters()
    {
        var cut = Render<FilterBar>(parameters => parameters
            .Add(component => component.Filters, builder =>
            {
                builder.OpenElement(0, "select");
                builder.AddAttribute(1, "class", "filter-bar__filter-select");
                builder.AddAttribute(2, "data-testid", "custom-filter");
                builder.CloseElement();
            }));

        cut.Find(".filter-bar__filters").Should().NotBeNull();
        cut.Find("[data-testid='custom-filter']").Should().NotBeNull();
    }

    private sealed class StubShortcutService : IShortcutService
    {
#pragma warning disable CS0067
        public event Action? ScopeChanged;
#pragma warning restore CS0067

        public void PushScope(string scopeId, ShortcutScopeType scopeType, IReadOnlyList<ScopeBinding> bindings)
        {
        }

        public void PopScope(string scopeId)
        {
        }

        public IReadOnlyDictionary<string, string> ComputeActiveBindings()
            => new Dictionary<string, string>();

        public Task ExecuteCommandAsync(string commandId)
            => Task.CompletedTask;

        public IReadOnlyList<CommandGroup> GetVisibleCommands(ICommandRegistry registry)
            => Array.Empty<CommandGroup>();
    }
}
