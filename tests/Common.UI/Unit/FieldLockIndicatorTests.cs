namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class FieldLockIndicatorTests : BunitContext
{
    private readonly FakeCollaborationService _collaboration = new();

    public FieldLockIndicatorTests()
    {
        Services.AddSingleton<ICollaborationService>(_collaboration);
    }

    [Fact]
    public void ShouldRenderChildContentWhenNotLocked()
    {
        var cut = RenderIndicator();

        cut.Find("[data-testid='child-input']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldNotShowLockedOverlayWhenFieldIsFree()
    {
        var cut = RenderIndicator();

        cut.FindAll("[data-testid='field-lock-amount-locked']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldShowLockedOverlayWhenFieldIsLocked()
    {
        _collaboration.SimulateFieldLock("Bob");

        var cut = RenderIndicator();

        var overlay = cut.Find("[data-testid='field-lock-amount-locked']");
        overlay.Should().NotBeNull();
        overlay.TextContent.Should().Contain("Bob");
    }

    [Fact]
    public void ShouldPassTrueToChildContentWhenLocked()
    {
        _collaboration.SimulateFieldLock("Bob");

        var cut = RenderIndicator();

        var input = cut.Find("[data-testid='child-input']");
        input.HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void ShouldPassFalseToChildContentWhenNotLocked()
    {
        var cut = RenderIndicator();

        var input = cut.Find("[data-testid='child-input']");
        input.HasAttribute("readonly").Should().BeFalse();
    }

    [Fact]
    public void ShouldHaveStatusRoleOnOverlay()
    {
        _collaboration.SimulateFieldLock("Alice");

        var cut = RenderIndicator();

        var overlay = cut.Find("[data-testid='field-lock-amount-locked']");
        overlay.GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void ShouldHaveAriaLabelWithLockerName()
    {
        _collaboration.SimulateFieldLock("Alice Martin");

        var cut = RenderIndicator();

        var overlay = cut.Find("[data-testid='field-lock-amount-locked']");
        overlay.GetAttribute("aria-label").Should().Be("Verrouillé par Alice Martin");
    }

    [Fact]
    public void ShouldUpdateWhenFieldPresenceChanges()
    {
        var cut = RenderIndicator();

        cut.FindAll("[data-testid='field-lock-amount-locked']").Count.Should().Be(0);

        _collaboration.SimulateFieldLock("Charlie");
        _collaboration.RaiseFieldPresenceChanged();

        cut.WaitForState(() =>
            cut.FindAll("[data-testid='field-lock-amount-locked']").Count > 0);

        cut.Find("[data-testid='field-lock-amount-locked']")
           .TextContent.Should().Contain("Charlie");
    }

    [Fact]
    public void ShouldRemoveOverlayWhenLockCleared()
    {
        _collaboration.SimulateFieldLock("Bob");

        var cut = RenderIndicator();
        cut.Find("[data-testid='field-lock-amount-locked']").Should().NotBeNull();

        _collaboration.ClearFieldLock();
        _collaboration.RaiseFieldPresenceChanged();

        cut.WaitForState(() =>
            cut.FindAll("[data-testid='field-lock-amount-locked']").Count == 0);
    }

    [Fact]
    public void ShouldRenderCustomTestId()
    {
        RenderFragment<bool> childContent = locked => builder =>
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "data-testid", "child-input");
            builder.CloseElement();
        };

        var cut = Render<FieldLockIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CircuitId, "circuit-self")
            .Add(b => b.TestId, "custom-lock")
            .Add(b => b.ChildContent, childContent));

        cut.Find("[data-testid='custom-lock']").Should().NotBeNull();
    }

    private IRenderedComponent<FieldLockIndicator> RenderIndicator()
    {
        RenderFragment<bool> childContent = locked => builder =>
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "data-testid", "child-input");
            if (locked)
            {
                builder.AddAttribute(2, "readonly", true);
            }

            builder.CloseElement();
        };

        return Render<FieldLockIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CircuitId, "circuit-self")
            .Add(b => b.TestId, "field-lock-amount")
            .Add(b => b.ChildContent, childContent));
    }

    private sealed class FakeCollaborationService : ICollaborationService
    {
        private readonly Dictionary<string, List<PresenceEntry>> _presence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FieldFocusEntry>> _fieldFocus = new(StringComparer.Ordinal);
        private string? _lockedBy;

#pragma warning disable CS0067
        public event Action? OnPresenceChanged;
#pragma warning restore CS0067

        public event Action? OnFieldPresenceChanged;

        public TimeSpan FieldLockTtl => TimeSpan.FromSeconds(60);

        public IReadOnlyList<PresenceEntry> GetPresence(string entityType, string entityId)
        {
            var key = $"{entityType}:{entityId}";
            return _presence.TryGetValue(key, out var entries) ? entries : [];
        }

        public IReadOnlyList<FieldFocusEntry> GetFieldPresence(string entityType, string entityId, string fieldName)
        {
            var key = $"{entityType}:{entityId}:{fieldName}";
            return _fieldFocus.TryGetValue(key, out var entries) ? entries : [];
        }

        public string? IsFieldLocked(string entityType, string entityId, string fieldName, string circuitId)
        {
            return _lockedBy;
        }

        public void Track(string entityType, string entityId, string circuitId, string user)
        {
        }

        public void Untrack(string circuitId)
        {
        }

        public void SetFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user)
        {
        }

        public void ClearFieldFocus(string circuitId, string? fieldName = null)
        {
        }

        public void RenewFieldFocus(string circuitId)
        {
        }

        public void PurgeExpiredEntries()
        {
        }

        public void SimulateFieldLock(string user)
        {
            _lockedBy = user;
        }

        public void ClearFieldLock()
        {
            _lockedBy = null;
        }

        public void RaiseFieldPresenceChanged()
        {
            OnFieldPresenceChanged?.Invoke();
        }
    }
}
