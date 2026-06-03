namespace Stratum.Common.UI.Services;

using System.Collections.Concurrent;
using Stratum.Common.Abstractions.Display;

/// <summary>
/// DI-registered singleton that resolves display templates for entity types.
/// Templates are registered via DI as <see cref="IDisplayTemplate{TEntity}"/>
/// by each module at startup (e.g. AddPartyModule registers PartyDisplayTemplate).
/// Falls back to ToString() when no template is registered.
/// </summary>
public sealed class DisplayTemplateRegistry : IDisplayTemplateRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, bool> _hasTemplateCache = new();
    private readonly ConcurrentDictionary<Type, Func<object, string>> _formatObjectCache = new();

    public DisplayTemplateRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Format<TEntity>(TEntity entity)
        where TEntity : notnull
    {
        var template = GetTemplate<TEntity>();
        return template is not null
            ? template.Format(entity)
            : entity.ToString() ?? string.Empty;
    }

    public bool HasTemplate<TEntity>()
    {
        return _hasTemplateCache.GetOrAdd(typeof(TEntity), _ =>
            _serviceProvider.GetService(typeof(IDisplayTemplate<TEntity>)) is not null);
    }

    public IDisplayTemplate<TEntity>? GetTemplate<TEntity>()
    {
        return _serviceProvider.GetService(typeof(IDisplayTemplate<TEntity>)) as IDisplayTemplate<TEntity>;
    }

    public string FormatObject(object entity)
    {
        var entityType = entity.GetType();
        var formatter = _formatObjectCache.GetOrAdd(entityType, type =>
        {
            var formatMethod = typeof(DisplayTemplateRegistry)
                .GetMethod(nameof(Format))!
                .MakeGenericMethod(type);
            return obj => (string)formatMethod.Invoke(this, [obj])!;
        });
        return formatter(entity);
    }
}
