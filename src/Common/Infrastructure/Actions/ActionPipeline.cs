namespace Stratum.Common.Infrastructure.Actions;

using Stratum.Common.Abstractions.Actions;

/// <summary>
/// Collects all registered <see cref="IActionStep{TEntity}"/> instances via DI,
/// sorts them by <see cref="ActionStage"/> then <see cref="IActionStep{TEntity}.Order"/>,
/// and executes them in sequence. Stops on the first step that returns an error-severity finding.
/// </summary>
internal sealed class ActionPipeline(IServiceProvider serviceProvider) : IActionPipeline
{
    public async Task<ActionResult> ExecuteAsync<TEntity>(ActionContext<TEntity> context)
    {
        var steps = ResolveSteps<TEntity>();

        if (steps.Count == 0)
        {
            return ActionResult.Success();
        }

        var allFindings = new List<ActionFinding>();

        foreach (var step in steps)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var result = await step.ExecuteAsync(context);

            if (result.Findings.Count > 0)
            {
                allFindings.AddRange(result.Findings);
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

    private List<IActionStep<TEntity>> ResolveSteps<TEntity>()
    {
        var steps = serviceProvider
            .GetService(typeof(IEnumerable<IActionStep<TEntity>>)) as IEnumerable<IActionStep<TEntity>>
            ?? [];

        return steps
            .OrderBy(s => (int)s.Stage)
            .ThenBy(s => s.Order)
            .ToList();
    }
}
