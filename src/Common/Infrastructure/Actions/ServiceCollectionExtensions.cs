namespace Stratum.Common.Infrastructure.Actions;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Validation;
using Stratum.Common.Infrastructure.Actions.Chains;
using Stratum.Common.Infrastructure.Actions.Hooks;
using Stratum.Common.Infrastructure.FieldChange;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumActionPipeline(this IServiceCollection services)
    {
        services.AddScoped<IActionPipeline, ActionPipeline>();
        services.AddScoped<IActionChainExecutor, ActionChainExecutor>();
        services.AddScoped<IHookExecutor, HookExecutor>();
        services.AddScoped<IFieldChangeEngine, FieldChangeEngine>();
        services.AddScoped(typeof(IFieldChangeNotifier<>), typeof(FieldChangeNotifier<>));

        // Scoped holder: the behavior writes via IActionPipelineResultWriter,
        // handlers read via IActionPipelineResult. Both resolve to the same instance.
        services.AddScoped<ActionPipelineResultHolder>();
        services.AddScoped<IActionPipelineResult>(sp => sp.GetRequiredService<ActionPipelineResultHolder>());
        services.AddScoped<IActionPipelineResultWriter>(sp => sp.GetRequiredService<ActionPipelineResultHolder>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="EntityValidationStep{TEntity}"/> as an action step for the specified entity type.
    /// This bridges <see cref="IValidationEngine"/> into the action pipeline at <see cref="ActionStage.PreValidation"/>.
    /// Call once per entity type that has <see cref="IEntityValidator{T}"/> registrations.
    /// </summary>
    public static IServiceCollection AddEntityValidationStep<TEntity>(this IServiceCollection services)
    {
        services.AddScoped<IActionStep<TEntity>, EntityValidationStep<TEntity>>();
        return services;
    }
}
