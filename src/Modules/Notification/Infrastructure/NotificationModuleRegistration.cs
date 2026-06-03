namespace Stratum.Modules.Notification.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Infrastructure.Services;

public static class NotificationModuleRegistration
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(INotificationApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(NotificationModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(NotificationModuleRegistration).Assembly));

        services.AddScoped<INotificationUnitOfWorkFactory, PostgresNotificationUnitOfWorkFactory>();
        services.AddScoped<IEmailTemplateQueries, PostgresEmailTemplateQueries>();
        services.AddScoped<INotificationSender, NotificationSender>();
        services.AddScoped<IEmailTransport, StubEmailTransport>();
        services.AddScoped<IWebhookQueries, PostgresWebhookQueries>();
        services.AddScoped<IRoutingRuleQueries, PostgresRoutingRuleQueries>();
        services.AddScoped<IServiceDefinitionQueries, PostgresServiceDefinitionQueries>();
        services.AddScoped<IRoutingEngine, RoutingEngine>();
        services.AddScoped<IDeliverySlaQueries, PostgresDeliverySlaQueries>();
        services.AddScoped<IDeliveryRecordQueries, PostgresDeliveryRecordQueries>();
        services.AddScoped<IApiKeyQueries, PostgresApiKeyQueries>();
        services.AddScoped<IIntegrationConfigQueries, PostgresIntegrationConfigQueries>();
        services.AddHostedService<NotificationEventTypeRegistrar>();
        services.AddHttpClient("WebhookDispatch")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));

        return services;
    }
}
