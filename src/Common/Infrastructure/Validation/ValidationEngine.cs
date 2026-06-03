namespace Stratum.Common.Infrastructure.Validation;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.Validation;

/// <summary>
/// Discovers all registered <see cref="IEntityValidator{T}"/> via DI and executes them,
/// aggregating all findings. Unlike the action pipeline, validation does NOT stop on error:
/// it collects every finding so the caller can present all problems at once.
/// </summary>
/// <remarks>
/// <see cref="IFieldValidator{T}"/> instances are resolved via DI but are not invoked during
/// full entity validation (they require a specific field name). They are intended for targeted
/// field-level validation during onChange or form-field blur scenarios.
/// </remarks>
internal sealed partial class ValidationEngine(IServiceProvider serviceProvider, ILogger<ValidationEngine> logger) : IValidationEngine
{
    public async Task<ValidationResult> ValidateAsync<T>(T entity, IActorContext actor, CancellationToken ct = default)
    {
        var validators = ResolveEntityValidators<T>();

        if (validators.Count == 0)
        {
            return ValidationResult.Valid();
        }

        var results = new List<ValidationResult>();

        foreach (var validator in validators)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await validator.ValidateAsync(entity, actor, ct);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogValidatorError(logger, validator.GetType().Name, ex);
                results.Add(ValidationResult.Invalid(
                    $"Validator {validator.GetType().Name} failed unexpectedly.",
                    code: "VAL-ENGINE-ERR"));
            }
        }

        return ValidationResult.Merge(results);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Entity validator {ValidatorType} threw an exception")]
    private static partial void LogValidatorError(ILogger logger, string validatorType, Exception exception);

    private List<IEntityValidator<T>> ResolveEntityValidators<T>()
    {
        return (serviceProvider.GetService(typeof(IEnumerable<IEntityValidator<T>>))
            as IEnumerable<IEntityValidator<T>> ?? []).ToList();
    }
}
