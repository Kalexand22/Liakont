namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class EntityPresenceLayoutTests : BunitContext
{
    private readonly FakeCollaborationService _collaboration = new();
    private readonly FakeEntityChangeSubscriber _subscriber = new();

    public EntityPresenceLayoutTests()
    {
        Services.AddSingleton<ICollaborationService>(_collaboration);
        Services.AddSingleton<IEntityChangeSubscriber>(_subscriber);
        Services.AddScoped<IToastService, ToastService>();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor("Test User"));
        Services.AddSingleton<CircuitPresenceRegistry>();
    }

    [Fact]
    public void ShouldRenderChildContent()
    {
        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42")
            .AddChildContent("<p data-testid='child'>Hello</p>"));

        cut.Find("[data-testid='child']").TextContent.Should().Be("Hello");
    }

    [Fact]
    public void ShouldTrackPresenceOnInit()
    {
        Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42"));

        var entries = _collaboration.GetPresence("Party", "42");
        entries.Should().HaveCount(1);
        entries[0].User.Should().Be("Test User");
    }

    [Fact]
    public async Task ShouldUntrackPresenceOnDispose()
    {
        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42"));

        _collaboration.GetPresence("Party", "42").Should().HaveCount(1);

        await cut.Instance.DisposeAsync();

        _collaboration.GetPresence("Party", "42").Should().BeEmpty();
    }

    [Fact]
    public void ShouldUntrackOnNavigation()
    {
        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42"));

        _collaboration.GetPresence("Party", "42").Should().HaveCount(1);

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/other-page");

        _collaboration.GetPresence("Party", "42").Should().BeEmpty();
    }

    [Fact]
    public void ShouldRenderPresenceBadge()
    {
        _collaboration.Track("Party", "42", "other-circuit", "Alice");

        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42"));

        cut.Find("[data-testid='presence-layout-badge']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithCustomTestId()
    {
        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42")
            .Add(l => l.TestId, "my-layout"));

        cut.Find("[data-testid='my-layout']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldUseFallbackDisplayNameWhenNull()
    {
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor(null));

        var cut = Render<EntityPresenceLayout>(p => p
            .Add(l => l.EntityType, "Party")
            .Add(l => l.EntityId, "42"));

        var entries = _collaboration.GetPresence("Party", "42");

        // Last registered entry should have fallback name
        entries.Should().Contain(e => e.User == "Utilisateur");
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        private readonly IActorContext _context;

        public FakeActorContextAccessor(string? displayName)
        {
            _context = new FakeActorContext(displayName);
        }

        public IActorContext Current => _context;

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(string? displayName) => DisplayName = displayName;

            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.NewGuid();

            public bool IsAuthenticated => true;

            public string? DisplayName { get; }

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }

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
    }
}
