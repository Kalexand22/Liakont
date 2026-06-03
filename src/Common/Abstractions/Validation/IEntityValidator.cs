namespace Stratum.Common.Abstractions.Validation;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Validates an entire entity instance. Implementations are discovered via DI
/// and executed by <see cref="IValidationEngine"/>.
/// </summary>
/// <typeparam name="T">The entity type to validate.</typeparam>
public interface IEntityValidator<in T>
{
    Task<ValidationResult> ValidateAsync(T entity, IActorContext actor, CancellationToken ct = default);
}
