namespace Stratum.Common.Infrastructure.Actions.Chains;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Validation;

/// <summary>
/// Discovers <see cref="IActionChain{TEntity}"/> registrations via DI, builds the step
/// sequence using <see cref="ActionChainBuilder{TEntity}"/>, and executes steps in order.
/// Validation and execution steps use stop-on-error semantics.
/// Notification steps never block the chain.
/// </summary>
internal sealed partial class ActionChainExecutor(
    IServiceProvider serviceProvider,
    ILogger<ActionChainExecutor> logger) : IActionChainExecutor
{
    public async Task<ActionResult> ExecuteChainAsync<TEntity>(
        string chainName,
        ActionContext<TEntity> context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainName);
        ArgumentNullException.ThrowIfNull(context);

        var chains = serviceProvider
            .GetService(typeof(IEnumerable<IActionChain<TEntity>>)) as IEnumerable<IActionChain<TEntity>>
            ?? [];

        var chain = chains.FirstOrDefault(c =>
            string.Equals(c.Name, chainName, StringComparison.Ordinal));

        if (chain is null)
        {
            LogChainNotFound(chainName, typeof(TEntity).Name);
            return ActionResult.Failure("chain", $"Action chain '{chainName}' not found for entity type '{typeof(TEntity).Name}'.");
        }

        var builder = new ActionChainBuilder<TEntity>();
        chain.Configure(builder);
        var steps = builder.Build();

        if (steps.Count == 0)
        {
            return ActionResult.Success();
        }

        var allFindings = new List<ActionFinding>();

        foreach (var step in steps)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteStepAsync(step, context);

            if (result.Findings.Count > 0)
            {
                allFindings.AddRange(result.Findings);
            }

            // Notification steps never block the chain
            if (step.Kind == ChainStepKind.Notify)
            {
                continue;
            }

            if (!result.IsSuccess)
            {
                return ActionResult.Failure(allFindings.AsReadOnly());
            }
        }

        return allFindings.Count > 0
            ? ActionResult.Success(allFindings.AsReadOnly())
            : ActionResult.Success();
    }

    private static ActionFinding ToActionFinding(ValidationFinding vf) =>
        new()
        {
            Severity = vf.Severity switch
            {
                ValidationSeverity.Error => ActionFindingSeverity.Error,
                ValidationSeverity.Warning => ActionFindingSeverity.Warning,
                _ => ActionFindingSeverity.Info,
            },
            Field = vf.Field,
            Message = vf.Message,
            Code = vf.Code,
        };

    private async Task<ActionResult> ExecuteStepAsync<TEntity>(
        ChainStepDescriptor step,
        ActionContext<TEntity> context)
    {
        // Evaluate condition — skip if predicate returns false
        if (step.Condition is Func<ActionContext<TEntity>, bool> predicate && !predicate(context))
        {
            return ActionResult.Success();
        }

        if (step.Kind == ChainStepKind.Validate)
        {
            return await ExecuteValidatorAsync(step, context);
        }

        // Execute or Notify — resolve as IActionStep<TEntity>
        return await ExecuteActionStepAsync<TEntity>(step, context);
    }

    private async Task<ActionResult> ExecuteValidatorAsync<TEntity>(
        ChainStepDescriptor step,
        ActionContext<TEntity> context)
    {
        var validator = serviceProvider.GetService(step.ServiceType) as IEntityValidator<TEntity>;

        if (validator is null)
        {
            LogStepResolutionFailed(step.ServiceType.Name, "Validate");
            return ActionResult.Failure("chain", $"Could not resolve validator '{step.ServiceType.Name}' from DI.");
        }

        var validationResult = await validator.ValidateAsync(context.Entity, context.Actor, context.CancellationToken);

        if (validationResult.IsValid)
        {
            return validationResult.Findings.Count > 0
                ? ActionResult.Success(validationResult.Findings
                    .Select(ToActionFinding)
                    .ToList()
                    .AsReadOnly())
                : ActionResult.Success();
        }

        return ActionResult.Failure(validationResult.Findings
            .Select(ToActionFinding)
            .ToList()
            .AsReadOnly());
    }

    private async Task<ActionResult> ExecuteActionStepAsync<TEntity>(
        ChainStepDescriptor step,
        ActionContext<TEntity> context)
    {
        var actionStep = serviceProvider.GetService(step.ServiceType) as IActionStep<TEntity>;

        if (actionStep is null)
        {
            LogStepResolutionFailed(step.ServiceType.Name, step.Kind.ToString());
            return ActionResult.Failure("chain", $"Could not resolve step '{step.ServiceType.Name}' from DI.");
        }

        return await actionStep.ExecuteAsync(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action chain '{ChainName}' not found for entity type '{EntityType}'.")]
    private partial void LogChainNotFound(string chainName, string entityType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve step '{StepType}' ({StepKind}) from DI.")]
    private partial void LogStepResolutionFailed(string stepType, string stepKind);
}
