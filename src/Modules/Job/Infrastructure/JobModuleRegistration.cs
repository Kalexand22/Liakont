namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Contracts.Queries;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Infrastructure.Queries;
using Stratum.Modules.Job.Infrastructure.Services;

public static class JobModuleRegistration
{
    public static IServiceCollection AddJobModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IJobApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(JobModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(JobModuleRegistration).Assembly));

        services.AddHostedService<JobEventTypeRegistrar>();

        services.AddScoped<IJobUnitOfWorkFactory, PostgresJobUnitOfWorkFactory>();
        services.AddScoped<IJobQueue, PostgresJobQueue>();
        services.AddScoped<IJobQueries, PostgresJobQueries>();

        // Job worker
        services.Configure<JobWorkerOptions>(configuration.GetSection(JobWorkerOptions.SectionName));
        services.AddSingleton<IJobHandlerResolver, JobHandlerResolver>();
        services.AddHostedService<JobWorker>();

        // Schedule services
        services.AddScoped<IScheduleUnitOfWorkFactory, PostgresScheduleUnitOfWorkFactory>();
        services.AddScoped<IScheduleQueries, PostgresScheduleQueries>();
        services.AddSingleton<ICronPreviewService, CronPreviewService>();

        // Job scheduler
        services.Configure<JobSchedulerOptions>(configuration.GetSection(JobSchedulerOptions.SectionName));
        services.AddHostedService<JobScheduler>();

        return services;
    }
}
