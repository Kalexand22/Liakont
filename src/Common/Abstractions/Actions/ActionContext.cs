namespace Stratum.Common.Abstractions.Actions;

using System.Collections.Immutable;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Context carried through the action pipeline for a given entity.
/// </summary>
public sealed record ActionContext<TEntity>
{
    public required TEntity Entity { get; init; }

    public required IActorContext Actor { get; init; }

    public IReadOnlySet<string> ChangedFields { get; init; } = ImmutableHashSet<string>.Empty;

    public CancellationToken CancellationToken { get; init; }
}
