namespace Stratum.Common.Abstractions.Validation;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Discovers and executes all registered <see cref="IEntityValidator{T}"/> and
/// <see cref="IFieldValidator{T}"/> for a given entity type.
/// </summary>
public interface IValidationEngine
{
    Task<ValidationResult> ValidateAsync<T>(T entity, IActorContext actor, CancellationToken ct = default);
}
