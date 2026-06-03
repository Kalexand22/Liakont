namespace Stratum.Common.Infrastructure.DataIsolation;

using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICompanyFilter"/> with the scoped implementation.
    /// Repositories use this to obtain the current company ID for data isolation.
    /// </summary>
    public static IServiceCollection AddStratumCompanyFilter(this IServiceCollection services)
    {
        services.AddScoped<ICompanyFilter, CompanyFilter>();
        return services;
    }
}
