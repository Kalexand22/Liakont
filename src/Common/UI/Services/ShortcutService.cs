namespace Stratum.Common.UI.Services;

using System.Linq;
using Stratum.Common.UI.Models;

/// <summary>
/// Scoped implementation of <see cref="ICommandRegistry"/> and <see cref="IShortcutService"/>.
/// One instance per Blazor circuit (SignalR connection).
/// </summary>
public sealed class ShortcutService : ICommandRegistry, IShortcutService
{
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.Ordinal);
    private readonly List<ActiveScope> _scopes = [];

    public event Action? Changed;

    public event Action? ScopeChanged;

    public void Register(CommandDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _commands[definition.Id] = definition;
        Changed?.Invoke();
    }

    public void Unregister(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (_commands.Remove(commandId))
        {
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<CommandDefinition> GetAll() => [.. _commands.Values];

    public void PushScope(string scopeId, ShortcutScopeType scopeType, IReadOnlyList<ScopeBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(bindings);
        _scopes.Add(new ActiveScope(scopeId, scopeType, bindings));
        ScopeChanged?.Invoke();
    }

    public void PopScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var idx = _scopes.FindLastIndex(s => s.ScopeId == scopeId);
        if (idx >= 0)
        {
            _scopes.RemoveAt(idx);
            ScopeChanged?.Invoke();
        }
    }

    /// <inheritdoc/>
    /// Scopes are sorted from lowest to highest priority so higher-priority scopes overwrite
    /// lower-priority entries for the same key in the resulting dictionary.
    public IReadOnlyDictionary<string, string> ComputeActiveBindings()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in _scopes.OrderBy(s => (int)s.Type))
        {
            foreach (var binding in scope.Bindings)
            {
                if (binding.Handler is not null)
                {
                    result[binding.KeyId] = binding.CommandId;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    /// Sorts the scope stack by type descending (Modal first, Global last) — consistent
    /// with <see cref="ComputeActiveBindings"/> which gives higher-type scopes precedence —
    /// then executes the handler in the first scope that declares the given command ID.
    public async Task ExecuteCommandAsync(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        foreach (var scope in _scopes.OrderByDescending(s => (int)s.Type))
        {
            var binding = scope.Bindings.FirstOrDefault(b => b.CommandId == commandId);
            if (binding?.Handler is { } handler)
            {
                await handler.Invoke();
                return;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CommandGroup> GetVisibleCommands(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var allCommands = registry.GetAll().ToDictionary(c => c.Id, StringComparer.Ordinal);
        var byScope = new Dictionary<ShortcutScopeType, List<(CommandDefinition, string)>>();

        foreach (var scope in _scopes)
        {
            if (!byScope.TryGetValue(scope.Type, out var list))
            {
                list = [];
                byScope[scope.Type] = list;
            }

            foreach (var binding in scope.Bindings)
            {
                if (allCommands.TryGetValue(binding.CommandId, out var def))
                {
                    if (!list.Any(x => x.Item1.Id == def.Id))
                    {
                        list.Add((def, binding.DisplayHint));
                    }
                }
            }
        }

        return [.. byScope
            .OrderBy(kvp => (int)kvp.Key)
            .Select(kvp =>
            {
                var label = kvp.Key switch
                {
                    ShortcutScopeType.Global => "Global",
                    ShortcutScopeType.Page => "Page",
                    ShortcutScopeType.Widget => "Widget",
                    ShortcutScopeType.Modal => "Modal",
                    _ => kvp.Key.ToString(),
                };
                return new CommandGroup(kvp.Key, label, kvp.Value);
            })];
    }

    private sealed record ActiveScope(string ScopeId, ShortcutScopeType Type, IReadOnlyList<ScopeBinding> Bindings);
}
