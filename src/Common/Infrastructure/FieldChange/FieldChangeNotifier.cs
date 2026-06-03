namespace Stratum.Common.Infrastructure.FieldChange;

using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Blazor-facing service that wraps <see cref="IFieldChangeEngine"/> with automatic
/// actor context resolution. Scoped per Blazor circuit. Components call this
/// instead of the engine directly.
/// </summary>
internal sealed class FieldChangeNotifier<T>(
    IFieldChangeEngine engine,
    IActorContextAccessor actorContextAccessor) : IFieldChangeNotifier<T>
{
    public Task<FieldChangeResult> NotifyAsync(
        T entity,
        IReadOnlySet<string> changedFields,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(changedFields);

        var actor = actorContextAccessor.Current;
        return engine.ProcessChangesAsync(entity, changedFields, actor, ct);
    }

    public Task<FieldChangeResult> NotifyAsync(
        T entity,
        string changedField,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(changedField);

        return NotifyAsync(entity, new HashSet<string> { changedField }, ct);
    }
}
