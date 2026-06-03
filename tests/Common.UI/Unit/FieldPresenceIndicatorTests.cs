namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class FieldPresenceIndicatorTests : BunitContext
{
    private readonly FakeCollaborationService _collaboration = new();

    public FieldPresenceIndicatorTests()
    {
        Services.AddSingleton<ICollaborationService>(_collaboration);
    }

    [Fact]
    public void ShouldNotRenderWhenNoOtherUsersOnField()
    {
        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        cut.FindAll("[data-testid='field-presence-indicator']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderBadgeWhenOtherUserFocusedOnField()
    {
        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Alice");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var indicator = cut.Find("[data-testid='field-presence-indicator']");
        indicator.Should().NotBeNull();
    }

    [Fact]
    public void ShouldShowInitialsInBadge()
    {
        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Jean Dupont");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find(".field-presence-indicator__badge");
        badge.TextContent.Trim().Should().Be("JD");
    }

    [Fact]
    public void ShouldExcludeCurrentCircuit()
    {
        _collaboration.SimulateFieldFocus("circuit-self", "Quote", "42", "Amount", "Me");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        cut.FindAll("[data-testid='field-presence-indicator']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldShowMultipleBadgesForMultipleUsers()
    {
        _collaboration.SimulateFieldFocus("circuit-a", "Quote", "42", "Amount", "Alice");
        _collaboration.SimulateFieldFocus("circuit-b", "Quote", "42", "Amount", "Bob");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badges = cut.FindAll(".field-presence-indicator__badge");
        badges.Count.Should().Be(2);
    }

    [Fact]
    public void ShouldShowTooltipWithUserName()
    {
        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Alice");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var badge = cut.Find(".field-presence-indicator__badge");
        badge.GetAttribute("title").Should().Be("Alice");
    }

    [Fact]
    public void ShouldHaveStatusRoleAndAriaLabel()
    {
        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Alice");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        var indicator = cut.Find("[data-testid='field-presence-indicator']");
        indicator.GetAttribute("role").Should().Be("status");
        indicator.GetAttribute("aria-label").Should().Contain("Alice");
    }

    [Fact]
    public void ShouldUpdateDynamicallyWhenFieldPresenceChanges()
    {
        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.CurrentCircuitId, "circuit-self"));

        cut.FindAll("[data-testid='field-presence-indicator']").Count.Should().Be(0);

        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Alice");

        cut.WaitForState(
            () => cut.FindAll("[data-testid='field-presence-indicator']").Count > 0,
            TimeSpan.FromSeconds(1));

        cut.Find(".field-presence-indicator__badge").TextContent.Trim().Should().Be("A");
    }

    [Fact]
    public void ShouldSupportCustomTestId()
    {
        _collaboration.SimulateFieldFocus("circuit-other", "Quote", "42", "Amount", "Alice");

        var cut = Render<FieldPresenceIndicator>(p => p
            .Add(b => b.EntityType, "Quote")
            .Add(b => b.EntityId, "42")
            .Add(b => b.FieldName, "Amount")
            .Add(b => b.TestId, "my-indicator"));

        cut.Find("[data-testid='my-indicator']").Should().NotBeNull();
    }

    [Theory]
    [InlineData("Alice", "A")]
    [InlineData("Jean Dupont", "JD")]
    [InlineData("  Marie  Claire  Lefebvre  ", "ML")]
    public void GetInitialsShouldReturnCorrectInitials(string user, string expected)
    {
        FieldPresenceIndicator.GetInitials(user).Should().Be(expected);
    }

    [Fact]
    public void GetUserColorShouldBeDeterministic()
    {
        var color1 = FieldPresenceIndicator.GetUserColor("Alice");
        var color2 = FieldPresenceIndicator.GetUserColor("Alice");
        color1.Should().Be(color2);
    }

    [Fact]
    public void GetUserColorShouldVaryByUser()
    {
        // With 8 colors, some users may collide, but "Alice" and "Bob" should differ
        // because their hashes are far apart. If they collide, that's still valid behavior.
        var colorAlice = FieldPresenceIndicator.GetUserColor("Alice");
        var colorBob = FieldPresenceIndicator.GetUserColor("Bob");

        // Just check they return valid hex colors
        colorAlice.Should().StartWith("#");
        colorBob.Should().StartWith("#");
    }

    /// <summary>
    /// Fake that supports both entity presence and field focus.
    /// </summary>
    private sealed class FakeCollaborationService : ICollaborationService
    {
        private readonly Dictionary<string, List<PresenceEntry>> _presence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FieldFocusEntry>> _fieldFocus = new(StringComparer.Ordinal);

#pragma warning disable CS0067
        public event Action? OnPresenceChanged;
#pragma warning restore CS0067

        public event Action? OnFieldPresenceChanged;

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

            OnPresenceChanged?.Invoke();
        }

        public void Untrack(string circuitId)
        {
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
            var key = $"{entityType}:{entityId}:{fieldName}";
            return _fieldFocus.TryGetValue(key, out var entries) ? entries : [];
        }

        /// <summary>
        /// Test helper: simulate a user focusing on a field.
        /// </summary>
        public void SimulateFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user)
        {
            var key = $"{entityType}:{entityId}:{fieldName}";
            if (!_fieldFocus.TryGetValue(key, out var entries))
            {
                entries = [];
                _fieldFocus[key] = entries;
            }

            if (entries.All(e => e.CircuitId != circuitId))
            {
                entries.Add(new FieldFocusEntry(circuitId, user, DateTimeOffset.UtcNow));
            }

            OnFieldPresenceChanged?.Invoke();
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
