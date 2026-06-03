namespace Stratum.Common.Infrastructure.Validation;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Validation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumValidationEngine(this IServiceCollection services)
    {
        services.AddScoped<IValidationEngine, ValidationEngine>();
        return services;
    }
}
