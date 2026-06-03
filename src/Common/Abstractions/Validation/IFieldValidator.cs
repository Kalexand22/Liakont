namespace Stratum.Common.Abstractions.Validation;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Validates a single field of an entity. Useful for targeted, field-level validation
/// with a specific error message.
/// </summary>
/// <remarks>
/// Callers should pass <paramref name="fieldName"/> using <c>nameof()</c> for compile-time safety
/// (e.g., <c>nameof(Company.Name)</c>). The engine implementation should verify that the field
/// name maps to a real property on <typeparamref name="T"/> at runtime.
/// </remarks>
/// <typeparam name="T">The entity type containing the field.</typeparam>
public interface IFieldValidator<in T>
{
    Task<ValidationResult> ValidateFieldAsync(
        T entity,
        string fieldName,
        IActorContext actor,
        CancellationToken ct = default);
}
