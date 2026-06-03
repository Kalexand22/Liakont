namespace Stratum.Common.Infrastructure.Database;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.AddOptionsWithValidateOnStart<DatabaseOptions>()
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Database:ConnectionString must be configured.");

        services.Configure<TenantConnectionOptions>(
            configuration.GetSection(TenantConnectionOptions.SectionName));

        services.AddOptionsWithValidateOnStart<TenantConnectionOptions>()
            .Validate(
                o => DatabasePrefixRegex().IsMatch(o.DatabasePrefix),
                "TenantConnections:DatabasePrefix must be 1-20 lowercase alphanumeric characters or underscores.");

        services.AddOptions<MigrationAssembliesOptions>();
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddSingleton<ISystemConnectionFactory>(sp => sp.GetRequiredService<NpgsqlConnectionFactory>());
        services.AddScoped<IConnectionFactory, TenantScopedConnectionFactory>();
        services.AddSingleton<NpgsqlDataSourceRegistry>();
        services.AddSingleton<ITenantConnectionFactory, TenantAwareNpgsqlConnectionFactory>();
        services.AddTransient<MigrationRunner>();
        services.AddTransient<ITenantProvisioningService, TenantProvisioningService>();
        services.AddTransient<TenantQueries>();
        services.AddTransient<ITenantQueries>(sp => sp.GetRequiredService<TenantQueries>());

        // Keycloak Admin API services for realm provisioning
        services.Configure<KeycloakAdminOptions>(
            configuration.GetSection(KeycloakAdminOptions.SectionName));
        services.AddHttpClient("KeycloakAdmin");
        services.AddSingleton<KeycloakAdminTokenService>();
        services.AddTransient<IKeycloakRealmProvisioner, KeycloakRealmProvisioner>();

        return services;
    }

    [GeneratedRegex(@"^[a-z0-9_]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabasePrefixRegex();
}
