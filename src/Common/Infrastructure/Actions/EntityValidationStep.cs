namespace Stratum.Common.Infrastructure.Actions;

using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Validation;

/// <summary>
/// Generic bridge that runs <see cref="IValidationEngine"/> as a
/// <see cref="IActionStep{TEntity}"/> at <see cref="ActionStage.PreValidation"/>.
/// Converts <see cref="ValidationResult"/> findings to <see cref="ActionResult"/> findings.
/// </summary>
internal sealed class EntityValidationStep<TEntity>(IValidationEngine validationEngine) : IActionStep<TEntity>
{
    public ActionStage Stage => ActionStage.PreValidation;

    public int Order => 0;

    public async Task<ActionResult> ExecuteAsync(ActionContext<TEntity> context)
    {
        var result = await validationEngine.ValidateAsync(
            context.Entity, context.Actor, context.CancellationToken);

        if (result.IsValid && result.Findings.Count == 0)
        {
            return ActionResult.Success();
        }

        var actionFindings = result.Findings
            .Select(f => new ActionFinding
            {
                Severity = MapSeverity(f.Severity),
                Field = f.Field,
                Message = f.Message,
                Code = f.Code,
            })
            .ToList()
            .AsReadOnly();

        return result.IsValid
            ? ActionResult.Success(actionFindings)
            : ActionResult.Failure(actionFindings);
    }

    private static ActionFindingSeverity MapSeverity(ValidationSeverity severity) =>
        severity switch
        {
            ValidationSeverity.Error => ActionFindingSeverity.Error,
            ValidationSeverity.Warning => ActionFindingSeverity.Warning,
            ValidationSeverity.Info => ActionFindingSeverity.Info,
            _ => ActionFindingSeverity.Error,
        };
}
