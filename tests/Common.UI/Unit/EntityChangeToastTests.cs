namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class EntityChangeToastTests : BunitContext
{
    private readonly FakeEntityChangeSubscriber _subscriber = new();

    public EntityChangeToastTests()
    {
        Services.AddSingleton<IEntityChangeSubscriber>(_subscriber);
        Services.AddScoped<IToastService, ToastService>();
    }

    [Fact]
    public void ShouldNotRenderInitially()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        cut.FindAll("[data-testid='entity-change-toast']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldSubscribeOnInit()
    {
        Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        _subscriber.HasSubscriber("c1").Should().BeTrue();
    }

    [Fact]
    public void ShouldUnsubscribeOnDispose()
    {
        Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        Dispose();

        _subscriber.HasSubscriber("c1").Should().BeFalse();
    }

    [Fact]
    public void ShouldShowToastWhenEntityChanged()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='entity-change-toast']").Count > 0,
            TimeSpan.FromSeconds(1));

        var toast = cut.Find("[data-testid='entity-change-toast']");
        toast.TextContent.Should().Contain("Alice");
        toast.TextContent.Should().Contain("modifié");
    }

    [Fact]
    public void ShouldHaveRechargerButton()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='entity-change-reload']").Count > 0,
            TimeSpan.FromSeconds(1));

        var btn = cut.Find("[data-testid='entity-change-reload']");
        btn.TextContent.Should().Contain("Recharger");
    }

    [Fact]
    public void ShouldInvokeReloadCallbackAndDismissToast()
    {
        var reloadCalled = false;

        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1")
            .Add(t => t.OnReloadRequested, () => { reloadCalled = true; }));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='entity-change-reload']").Count > 0,
            TimeSpan.FromSeconds(1));

        cut.Find("[data-testid='entity-change-reload']").Click();

        reloadCalled.Should().BeTrue();
        cut.FindAll("[data-testid='entity-change-toast']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldDismissOnDismissButtonClick()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='entity-change-dismiss']").Count > 0,
            TimeSpan.FromSeconds(1));

        cut.Find("[data-testid='entity-change-dismiss']").Click();

        cut.FindAll("[data-testid='entity-change-toast']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldHaveAlertRoleAndAriaLive()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1"));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='entity-change-toast']").Count > 0,
            TimeSpan.FromSeconds(1));

        var toast = cut.Find("[data-testid='entity-change-toast']");
        toast.GetAttribute("role").Should().Be("alert");
        toast.GetAttribute("aria-live").Should().Be("assertive");
    }

    [Fact]
    public void ShouldSupportCustomTestId()
    {
        var cut = Render<EntityChangeToast>(p => p
            .Add(t => t.CircuitId, "c1")
            .Add(t => t.TestId, "my-toast"));

        _subscriber.Notify("c1", new EntityChangedEvent("Quote", "42", "Alice", DateTimeOffset.UtcNow));

        cut.WaitForState(
            () => cut.FindAll("[data-testid='my-toast']").Count > 0,
            TimeSpan.FromSeconds(1));

        cut.Find("[data-testid='my-toast']").Should().NotBeNull();
    }

    private sealed class FakeEntityChangeSubscriber : IEntityChangeSubscriber
    {
        private readonly Dictionary<string, Action<EntityChangedEvent>> _callbacks = new(StringComparer.Ordinal);

        public void Subscribe(string circuitId, Action<EntityChangedEvent> callback)
        {
            _callbacks[circuitId] = callback;
        }

        public void Unsubscribe(string circuitId)
        {
            _callbacks.Remove(circuitId);
        }

        public bool HasSubscriber(string circuitId)
        {
            return _callbacks.ContainsKey(circuitId);
        }

        public void Notify(string circuitId, EntityChangedEvent evt)
        {
            if (_callbacks.TryGetValue(circuitId, out var callback))
            {
                callback(evt);
            }
        }
    }
}
