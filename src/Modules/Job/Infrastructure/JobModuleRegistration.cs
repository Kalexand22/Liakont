namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Liakont addition (FIX211) : admin des jobs utilisable. Catalogue des types enregistrés (liste fixe +
        // libellés FR, depuis les JobHandlerRegistration singletons) et read-model des exécutions (job.jobs,
        // tenant-scopé). Le catalogue est singleton : il agrège les enregistrements résolus après le bootstrap.
        services.AddSingleton<IJobTypeCatalog, JobTypeCatalog>();
        services.AddScoped<IJobExecutionsQueries, PostgresJobExecutionsQueries>();

        // Job worker
        services.Configure<JobWorkerOptions>(configuration.GetSection(JobWorkerOptions.SectionName));
        services.AddSingleton<IJobHandlerResolver, JobHandlerResolver>();
        services.AddHostedService<JobWorker>();

        // Schedule services
        services.AddScoped<IScheduleUnitOfWorkFactory, PostgresScheduleUnitOfWorkFactory>();
        services.AddScoped<IScheduleQueries, PostgresScheduleQueries>();
        services.AddSingleton<ICronPreviewService, CronPreviewService>();

        // Liakont addition (BUG-4b) : défaut no-op de la société porteuse des jobs SYSTÈME (socle
        // auto-suffisant). TryAdd → le Host produit l'écrase par une implémentation qui connaît les jobs de
        // fan-out plateforme, rendant les jobs système planifiables/consultables par un opérateur plateforme.
        services.TryAddSingleton<ISystemScheduleHost, NullSystemScheduleHost>();

        // Job scheduler
        services.Configure<JobSchedulerOptions>(configuration.GetSection(JobSchedulerOptions.SectionName));
        services.AddHostedService<JobScheduler>();

        return services;
    }
}
