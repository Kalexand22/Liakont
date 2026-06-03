namespace Stratum.Common.Infrastructure.UiRules;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.UiRules;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumUiRuleEngine(this IServiceCollection services)
    {
        services.AddSingleton<IUiRuleEngine, UiRuleEngine>();
        services.AddScoped(typeof(IUiRuleService<>), typeof(UiRuleService<>));
        return services;
    }
}
