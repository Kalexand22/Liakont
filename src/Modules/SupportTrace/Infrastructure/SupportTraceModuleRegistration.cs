namespace Liakont.Modules.SupportTrace.Infrastructure;

using System;
using System.IO;
using Liakont.Modules.SupportTrace.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du module SupportTrace (FX06, F16 §7) : store de la trace de support du Factur-X
/// transmis et service de purge par rétention. Le store par défaut est <see cref="FileSystemSupportTraceStore"/>
/// (appliance) ; un plug-in de store (S3, …) le remplacera via <c>Replace</c>, comme le coffre d'archive et
/// le magasin de staging. La racine et la rétention sont du PARAMÉTRAGE d'INSTANCE (section
/// <c>SupportTrace</c>) — jamais en dur (CLAUDE.md n°7).
///
/// Le handler du job de purge (<see cref="SupportTracePurgeFanOutHandler"/>) est enregistré au composition
/// root (Host) via <c>AddJobHandler</c>, comme l'ancrage quotidien du coffre ; sa PLANIFICATION (cron) reste
/// un geste opérateur (housekeeping d'une rétention courte — aucune cadence inventée). <see cref="TimeProvider"/>
/// est fourni par le Host (horloge partagée).
/// </summary>
public static class SupportTraceModuleRegistration
{
    /// <summary>Enregistre le store de trace de support FileSystem et le service de purge par rétention.</summary>
    /// <param name="services">La collection de services.</param>
    /// <param name="configuration">La configuration de l'instance.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddSupportTraceModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SupportTraceOptions>(configuration.GetSection("SupportTrace"));
        services.PostConfigure<SupportTraceOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                // Repli sous le répertoire de l'instance ; une instance de production configure un volume dédié.
                options.RootPath = Path.Combine(AppContext.BaseDirectory, "support-trace-store");
            }

            // Garde de sûreté : une rétention non positive (mauvais paramétrage) purgerait tout → repli au
            // défaut sourcé F16 §10. Le service de purge refuse aussi une rétention non positive (défense en profondeur).
            if (options.RetentionDays <= 0)
            {
                options.RetentionDays = SupportTraceOptions.DefaultRetentionDays;
            }
        });

        // Store par défaut (FileSystem). Un plug-in de store le remplace via Replace.
        services.TryAddScoped<ISupportTraceStore, FileSystemSupportTraceStore>();
        services.AddScoped<ISupportTracePurgeService, SupportTracePurgeService>();

        return services;
    }
}
