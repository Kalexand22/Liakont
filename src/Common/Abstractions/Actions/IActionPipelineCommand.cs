namespace Stratum.Common.Abstractions.Actions;

using System.Collections.Immutable;

/// <summary>
/// Interface implemented by MediatR commands that participate in the action pipeline.
/// The command provides the entity to validate and any changed fields.
/// </summary>
/// <typeparam name="TEntity">The entity type that the pipeline steps operate on.</typeparam>
public interface IActionPipelineCommand<TEntity>
{
    /// <summary>
    /// Returns the entity instance that the pipeline steps will validate and process.
    /// </summary>
    TEntity GetPipelineEntity();

    /// <summary>
    /// Returns the set of fields that have changed, enabling targeted
    /// validation and field-change handling. Defaults to empty (full validation).
    /// </summary>
    IReadOnlySet<string> GetChangedFields() => ImmutableHashSet<string>.Empty;
}
