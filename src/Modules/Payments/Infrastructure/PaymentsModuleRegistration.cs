namespace Liakont.Modules.Payments.Infrastructure;

using Liakont.Modules.Payments.Application;
using Liakont.Modules.Payments.Contracts.Queries;
using Liakont.Modules.Payments.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Payments (F09, item TRK04) : migrations DbUp (schéma payments + tables +
/// piste d'audit append-only), fabrique d'unités de travail et requêtes de lecture. L'agrégation et la
/// transmission (handlers) arrivent avec le pipeline (PIP03) ; ce module porte le modèle et la persistance.
/// </summary>
public static class PaymentsModuleRegistration
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        // Anchor MediatR du module : les handlers (agrégation/transmission, PIP03) arrivent ensuite.
        // L'enregistrement du pipeline est fait dès maintenant pour rester homogène avec les autres modules.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IPaymentsApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(PaymentsModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(PaymentsModuleRegistration).Assembly));

        services.AddScoped<IPaymentUnitOfWorkFactory, PostgresPaymentUnitOfWorkFactory>();
        services.AddScoped<IPaymentQueries, PostgresPaymentQueries>();

        return services;
    }
}
