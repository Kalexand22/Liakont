namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Verifies the fallback contract: when <see cref="IGridPreferenceService"/> returns null,
/// the consumer resolves visible columns from <see cref="IColumnRegistry{TItem}.GetDefaultVisibleColumns"/>.
/// Also exercises the CRUD contract via an in-memory implementation.
/// </summary>
public sealed class GridPreferenceFallbackTests
{
    [Fact]
    public async Task ShouldUseDefaultVisibleColumnsWhenNoPreferenceStored()
    {
        var service = new StubGridPreferenceService(preference: null);
        var registry = new TestColumnRegistry();

        var pref = await service.GetPreferenceAsync(Guid.NewGuid(), "Test.Grid.Main");

        // Consumers must treat an empty ColumnKeys list as "no explicit columns"
        // and fall back to registry defaults, not hold onto the empty list.
        var visibleKeys = pref is { ColumnKeys.Count: > 0 }
            ? pref.ColumnKeys
            : registry.GetDefaultVisibleColumns().Select(c => c.Key).ToList();

        visibleKeys.Should().BeEquivalentTo(["Name", "Email"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ShouldFallBackToDefaultsWhenPreferenceRowExistsWithEmptyColumnKeys()
    {
        // A preference row may exist with empty ColumnKeys when the user
        // persisted widths-only (GUX09) before any column-visibility save.
        // Consumers must still fall back to registry defaults for visibility.
        var storedKeys = (IReadOnlyList<string>)Array.Empty<string>();
        var pref = new UserGridPreference(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test.Grid.Main",
            storedKeys,
            DateTimeOffset.UtcNow,
            null,
            ColumnWidths: new Dictionary<string, string> { ["Name"] = "240px" });
        var service = new StubGridPreferenceService(preference: pref);
        var registry = new TestColumnRegistry();

        var result = await service.GetPreferenceAsync(pref.UserId, "Test.Grid.Main");

        var visibleKeys = result is { ColumnKeys.Count: > 0 }
            ? result.ColumnKeys
            : registry.GetDefaultVisibleColumns().Select(c => c.Key).ToList();

        visibleKeys.Should().BeEquivalentTo(["Name", "Email"], o => o.WithStrictOrdering());
        result!.ColumnWidths!.Should().ContainKey("Name").WhoseValue.Should().Be("240px");
    }

    [Fact]
    public async Task SavedColumnWidthsRoundTripThroughGetPreference()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();
        var gridKey = "Party.PartyList.Main";
        var widths = new Dictionary<string, string>
        {
            ["Name"] = "240px",
            ["Email"] = "180px",
        };

        await service.SaveColumnWidthsAsync(userId, gridKey, widths);

        var pref = await service.GetPreferenceAsync(userId, gridKey);
        pref.Should().NotBeNull();
        pref!.ColumnWidths.Should().NotBeNull();
        pref.ColumnWidths!.Should().ContainKey("Name").WhoseValue.Should().Be("240px");
        pref.ColumnWidths!.Should().ContainKey("Email").WhoseValue.Should().Be("180px");
    }

    [Fact]
    public async Task SaveColumnWidthsShouldOverwriteExistingWidthsOnly()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();
        var gridKey = "Party.PartyList.Main";

        // Column keys persisted via the sibling method must survive a widths-only update.
        await service.SavePreferenceAsync(userId, gridKey, new List<string> { "Name", "Email" });

        await service.SaveColumnWidthsAsync(
            userId,
            gridKey,
            new Dictionary<string, string> { ["Name"] = "260px" });

        var pref = await service.GetPreferenceAsync(userId, gridKey);
        pref!.ColumnKeys.Should().BeEquivalentTo(["Name", "Email"], o => o.WithStrictOrdering());
        pref.ColumnWidths!.Should().ContainKey("Name").WhoseValue.Should().Be("260px");
    }

    [Fact]
    public async Task SaveColumnWidthsWithEmptyDictionaryClearsWidths()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();
        var gridKey = "Party.PartyList.Main";

        await service.SaveColumnWidthsAsync(
            userId,
            gridKey,
            new Dictionary<string, string> { ["Name"] = "260px" });

        await service.SaveColumnWidthsAsync(
            userId,
            gridKey,
            new Dictionary<string, string>());

        var pref = await service.GetPreferenceAsync(userId, gridKey);
        pref!.ColumnWidths.Should().NotBeNull();
        pref.ColumnWidths!.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldUseStoredPreferenceWhenPreferenceExists()
    {
        var storedKeys = new List<string> { "Email", "Phone", "Name" }.AsReadOnly();
        var pref = new UserGridPreference(
            Guid.NewGuid(), Guid.NewGuid(), "Test.Grid.Main", storedKeys, DateTimeOffset.UtcNow, null);
        var service = new StubGridPreferenceService(preference: pref);
        var registry = new TestColumnRegistry();

        var result = await service.GetPreferenceAsync(pref.UserId, "Test.Grid.Main");

        var visibleKeys = result?.ColumnKeys
            ?? registry.GetDefaultVisibleColumns().Select(c => c.Key).ToList();

        visibleKeys.Should().BeEquivalentTo(["Email", "Phone", "Name"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SavePreferenceShouldMakeSubsequentGetReturnSavedKeys()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();
        var gridKey = "Party.PartyList.Main";
        var keys = new List<string> { "LegalName", "TaxId" }.AsReadOnly();

        await service.SavePreferenceAsync(userId, gridKey, keys);
        var result = await service.GetPreferenceAsync(userId, gridKey);

        result.Should().NotBeNull();
        result!.ColumnKeys.Should().BeEquivalentTo(keys, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SavePreferenceShouldOverwriteExistingPreference()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();
        var gridKey = "Party.PartyList.Main";

        await service.SavePreferenceAsync(userId, gridKey, new List<string> { "A", "B" });
        await service.SavePreferenceAsync(userId, gridKey, new List<string> { "C", "D", "E" });

        var result = await service.GetPreferenceAsync(userId, gridKey);
        result!.ColumnKeys.Should().BeEquivalentTo(["C", "D", "E"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task GetPreferenceShouldReturnNullWhenDifferentGridKey()
    {
        var service = new InMemoryGridPreferenceService();
        var userId = Guid.NewGuid();

        await service.SavePreferenceAsync(userId, "Sales.InvoiceList.Main", new List<string> { "A" });

        var result = await service.GetPreferenceAsync(userId, "Party.PartyList.Main");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPreferenceShouldReturnNullWhenDifferentUser()
    {
        var service = new InMemoryGridPreferenceService();
        var gridKey = "Sales.InvoiceList.Main";

        await service.SavePreferenceAsync(Guid.NewGuid(), gridKey, new List<string> { "A" });

        var result = await service.GetPreferenceAsync(Guid.NewGuid(), gridKey);
        result.Should().BeNull();
    }

    private sealed class StubGridPreferenceService : IGridPreferenceService
    {
        private readonly UserGridPreference? _preference;

        public StubGridPreferenceService(UserGridPreference? preference) => _preference = preference;

        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default)
            => Task.FromResult(_preference);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// In-memory implementation for CRUD testing without a database.
    /// </summary>
    private sealed class InMemoryGridPreferenceService : IGridPreferenceService
    {
        private readonly Dictionary<(Guid UserId, string GridKey), UserGridPreference> _store = new();

        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default)
        {
            _store.TryGetValue((userId, gridKey), out var pref);
            return Task.FromResult(pref);
        }

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            _store.TryGetValue((userId, gridKey), out var existing);
            _store[(userId, gridKey)] = new UserGridPreference(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                gridKey,
                columnKeys,
                existing?.CreatedAt ?? now,
                existing is not null ? now : null,
                PreferredViewKind: existing?.PreferredViewKind,
                FilterStateJson: existing?.FilterStateJson,
                ColumnWidths: existing?.ColumnWidths);
            return Task.CompletedTask;
        }

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            _store.TryGetValue((userId, gridKey), out var existing);
            _store[(userId, gridKey)] = new UserGridPreference(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                gridKey,
                existing?.ColumnKeys ?? Array.Empty<string>(),
                existing?.CreatedAt ?? now,
                existing is not null ? now : null,
                PreferredViewKind: viewKind,
                FilterStateJson: existing?.FilterStateJson,
                ColumnWidths: existing?.ColumnWidths);
            return Task.CompletedTask;
        }

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            _store.TryGetValue((userId, gridKey), out var existing);
            _store[(userId, gridKey)] = new UserGridPreference(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                gridKey,
                existing?.ColumnKeys ?? Array.Empty<string>(),
                existing?.CreatedAt ?? now,
                existing is not null ? now : null,
                PreferredViewKind: existing?.PreferredViewKind,
                FilterStateJson: filterStateJson,
                ColumnWidths: existing?.ColumnWidths);
            return Task.CompletedTask;
        }

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            _store.TryGetValue((userId, gridKey), out var existing);

            // Copy into a new dictionary so callers can continue mutating their source
            // without silently patching the persisted snapshot.
            var snapshot = new Dictionary<string, string>(columnWidths);
            _store[(userId, gridKey)] = new UserGridPreference(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                gridKey,
                existing?.ColumnKeys ?? Array.Empty<string>(),
                existing?.CreatedAt ?? now,
                existing is not null ? now : null,
                PreferredViewKind: existing?.PreferredViewKind,
                FilterStateJson: existing?.FilterStateJson,
                ColumnWidths: snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class TestColumnRegistry : ColumnRegistryBase<TestDto>
    {
        protected override void Configure()
        {
            Column("Name", "Name", "Test", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            Column("Email", "Email", "Test", ColumnDataType.Text, defaultVisible: true, sortOrder: 20);
            Column("Phone", "Phone", "Test", ColumnDataType.Text, defaultVisible: false, sortOrder: 30);
            Column("Notes", "Notes", "Test", ColumnDataType.Text, defaultVisible: false, sortOrder: 40);
        }
    }

    private sealed class TestDto;
}
