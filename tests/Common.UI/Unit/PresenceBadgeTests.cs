namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class PresenceBadgeTests : BunitContext
{
    private readonly FakeCollaborationService _collaboration = new();

    public PresenceBadgeTests()
    {
        Services.AddSingleton<ICollaborationService>(_collaboration);
    }

    [Fact]
    public void ShouldNotRenderWhenNoOtherUsers()
    {
        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        cut.FindAll("[data-testid='presence-badge']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderWhenOtherUsersPresent()
    {
        _collaboration.Track("Quote", "42", "circuit-other", "Alice");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find("[data-testid='presence-badge']");
        badge.Should().NotBeNull();
        badge.TextContent.Should().Contain("1 autre utilisateur");
    }

    [Fact]
    public void ShouldExcludeCurrentCircuitFromCount()
    {
        _collaboration.Track("Quote", "42", "circuit-self", "Me");
        _collaboration.Track("Quote", "42", "circuit-other", "Alice");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find("[data-testid='presence-badge']");
        badge.TextContent.Should().Contain("1 autre utilisateur");
    }

    [Fact]
    public void ShouldShowAllUsersWhenNoCurrentCircuitId()
    {
        _collaboration.Track("Quote", "42", "circuit-a", "Alice");
        _collaboration.Track("Quote", "42", "circuit-b", "Bob");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42"));

        var badge = cut.Find("[data-testid='presence-badge']");
        badge.TextContent.Should().Contain("2 autres utilisateurs");
    }

    [Fact]
    public void ShouldShowTooltipWithUserNames()
    {
        _collaboration.Track("Quote", "42", "circuit-a", "Alice");
        _collaboration.Track("Quote", "42", "circuit-b", "Bob");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find("[data-testid='presence-badge']");
        var title = badge.GetAttribute("title") ?? string.Empty;
        title.Should().Contain("Alice");
        title.Should().Contain("Bob");
    }

    [Fact]
    public void ShouldHaveStatusRoleAndAriaLabel()
    {
        _collaboration.Track("Quote", "42", "circuit-other", "Alice");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find("[data-testid='presence-badge']");
        badge.GetAttribute("role").Should().Be("status");
        badge.GetAttribute("aria-label").Should().Contain("Alice");
    }

    [Fact]
    public void ShouldUpdateDynamicallyWhenPresenceChanges()
    {
        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        cut.FindAll("[data-testid='presence-badge']").Count.Should().Be(0);

        _collaboration.Track("Quote", "42", "circuit-other", "Alice");

        cut.WaitForState(
            () => cut.FindAll("[data-testid='presence-badge']").Count > 0,
            TimeSpan.FromSeconds(1));

        cut.Find("[data-testid='presence-badge']").TextContent
            .Should().Contain("1 autre utilisateur");
    }

    [Fact]
    public void ShouldSupportCustomTestId()
    {
        _collaboration.Track("Quote", "42", "circuit-other", "Alice");

        var cut = Render<PresenceBadge>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.TestId, "my-badge"));

        cut.Find("[data-testid='my-badge']").Should().NotBeNull();
    }

    /// <summary>
    /// Minimal fake for testing presence tracking.
    /// </summary>
    private sealed class FakeCollaborationService : ICollaborationService
    {
        private readonly Dictionary<string, List<PresenceEntry>> _presence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _entitiesByCircuit = new(StringComparer.Ordinal);

        public event Action? OnPresenceChanged;

#pragma warning disable CS0067
        public event Action? OnFieldPresenceChanged;
#pragma warning restore CS0067

        public TimeSpan FieldLockTtl => TimeSpan.FromSeconds(60);

        public void Track(string entityType, string entityId, string circuitId, string user)
        {
            var key = $"{entityType}:{entityId}";
            if (!_presence.TryGetValue(key, out var entries))
            {
                entries = [];
                _presence[key] = entries;
            }

            if (entries.All(e => e.CircuitId != circuitId))
            {
                entries.Add(new PresenceEntry(circuitId, user));
            }

            if (!_entitiesByCircuit.TryGetValue(circuitId, out var keys))
            {
                keys = [];
                _entitiesByCircuit[circuitId] = keys;
            }

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }

            OnPresenceChanged?.Invoke();
        }

        public void Untrack(string circuitId)
        {
            if (!_entitiesByCircuit.Remove(circuitId, out var keys))
            {
                return;
            }

            foreach (var key in keys)
            {
                if (_presence.TryGetValue(key, out var entries))
                {
                    entries.RemoveAll(e => e.CircuitId == circuitId);
                    if (entries.Count == 0)
                    {
                        _presence.Remove(key);
                    }
                }
            }

            OnPresenceChanged?.Invoke();
        }

        public IReadOnlyList<PresenceEntry> GetPresence(string entityType, string entityId)
        {
            var key = $"{entityType}:{entityId}";
            return _presence.TryGetValue(key, out var entries) ? entries : [];
        }

        public void SetFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user)
        {
        }

        public void ClearFieldFocus(string circuitId, string? fieldName = null)
        {
        }

        public IReadOnlyList<FieldFocusEntry> GetFieldPresence(string entityType, string entityId, string fieldName)
        {
            return [];
        }

        public string? IsFieldLocked(string entityType, string entityId, string fieldName, string circuitId)
        {
            return null;
        }

        public void RenewFieldFocus(string circuitId)
        {
        }

        public void PurgeExpiredEntries()
        {
        }
    }
}
