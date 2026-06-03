namespace Stratum.Common.UI.Services;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

public sealed class FormRegistry : IFormRegistry
{
    private readonly ConcurrentDictionary<(Type EntityType, string? ContextKey), Type> _registrations = new();

    /// <summary>Builds the registry from DI-supplied registrations. When two registrations share the same (entity, context) key, the last one wins (consistent with <see cref="Register{TEntity, TForm}"/> and DI singleton conventions).</summary>
    public FormRegistry(IEnumerable<FormRegistration> registrations)
    {
        foreach (var r in registrations)
        {
            _registrations[(r.EntityType, r.ContextKey)] = r.FormType;
        }
    }

    public void Register<TEntity, TForm>(string? contextKey = null)
        where TForm : ComponentBase
    {
        _registrations[(typeof(TEntity), contextKey)] = typeof(TForm);
    }

    public Type Resolve<TEntity>(string? contextKey = null)
    {
        if (TryResolve<TEntity>(contextKey, out var formType))
        {
            return formType;
        }

        throw new InvalidOperationException(
            $"No form registered for entity '{typeof(TEntity).Name}'" +
            (contextKey is not null ? $" with context '{contextKey}'" : string.Empty) +
            ". Register a form via IFormRegistry.Register<TEntity, TForm>().");
    }

    public bool TryResolve<TEntity>(string? contextKey, [MaybeNullWhen(false)] out Type formType)
    {
        if (contextKey is not null &&
            _registrations.TryGetValue((typeof(TEntity), contextKey), out formType))
        {
            return true;
        }

        if (_registrations.TryGetValue((typeof(TEntity), null), out formType))
        {
            return true;
        }

        formType = null;
        return false;
    }
}
