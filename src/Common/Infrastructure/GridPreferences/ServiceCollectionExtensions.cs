namespace Stratum.Common.Infrastructure.GridPreferences;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IGridPreferenceService"/> backed by PostgreSQL.
    /// </summary>
    public static IServiceCollection AddGridPreferences(this IServiceCollection services)
    {
        services.AddScoped<IGridPreferenceService, PostgresGridPreferenceService>();
        services.AddScoped<ISavedFilterService, PostgresSavedFilterService>();
        return services;
    }
}
