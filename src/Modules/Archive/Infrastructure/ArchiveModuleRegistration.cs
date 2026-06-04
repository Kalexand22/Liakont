namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.IO;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>
/// Enregistrement DI du module Archive (TRK05 + TRK06) : coffre WORM, chaîne de hashes, addenda chaînés,
/// ancrage temporel, vérifieur d'intégrité et export contrôle fiscal. N'apporte AUCUNE migration : il
/// alimente <c>documents.archive_entries</c> (TRK01) et <c>documents.archive_anchors</c> (V006, créées par
/// le module Documents). Le store par défaut est <see cref="FileSystemArchiveStore"/> ; l'ancrage par
/// défaut est <see cref="NoAnchorTimestampAnchor"/> — une instance hébergée active RFC 3161 par
/// configuration (<c>Archive:Anchor</c>). Les deux choix sont du paramétrage d'INSTANCE (blueprint §6, §2 règle 6).
/// </summary>
public static class ArchiveModuleRegistration
{
    public static IServiceCollection AddArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FileSystemArchiveStoreOptions>(configuration.GetSection("Archive:Storage:FileSystem"));
        services.PostConfigure<FileSystemArchiveStoreOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                // Repli sous le répertoire de l'instance ; une instance de production configure un volume dédié.
                options.RootPath = Path.Combine(AppContext.BaseDirectory, "archive-store");
            }
        });

        // Store par défaut (FileSystem). Un plug-in de store (S3, Azure, GCS) le remplace via Replace.
        services.TryAddScoped<IArchiveStore, FileSystemArchiveStore>();

        services.AddScoped<IArchiveEntryStore, PostgresArchiveEntryStore>();
        services.AddScoped<IArchiveService, ArchiveService>();

        AddAnchoring(services, configuration);

        return services;
    }

    /// <summary>
    /// Ancrage temporel (TRK06) : ancrage choisi par config d'instance (NoAnchor par défaut), vérifieur
    /// d'intégrité, export contrôle fiscal et handler de fan-out du job quotidien. Le job système lui-même
    /// est planifié côté Host (AddJobHandler + JobScheduler) — voir ADR-0010.
    /// </summary>
    private static void AddAnchoring(IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(TimestampAnchorOptions.SectionName);
        services.Configure<TimestampAnchorOptions>(section);

        string method = section["Method"] ?? "None";
        if (string.Equals(method, "Rfc3161", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(HttpTsaClient.HttpClientName)
                .ConfigureHttpClient((sp, client) =>
                {
                    TimestampAnchorOptions options = sp.GetRequiredService<IOptions<TimestampAnchorOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Rfc3161.TimeoutSeconds));
                });
            services.TryAddSingleton<ITsaClient, HttpTsaClient>();
            services.TryAddSingleton<ITimestampAnchor, Rfc3161TimestampAnchor>();
        }
        else if (string.Equals(method, "OpenTimestamps", StringComparison.OrdinalIgnoreCase))
        {
            // Présent mais non opérationnel en V1 (ADR-0010) : lève à l'usage, jamais un no-op silencieux.
            services.TryAddSingleton<ITimestampAnchor, OpenTimestampsTimestampAnchor>();
        }
        else
        {
            // Défaut : aucune sortie réseau. L'intégrité reste portée par la chaîne de hashes.
            services.TryAddSingleton<ITimestampAnchor, NoAnchorTimestampAnchor>();
        }

        services.AddScoped<IArchiveAnchorStore, PostgresArchiveAnchorStore>();
        services.AddScoped<IArchiveAnchoringService, ArchiveAnchoringService>();
        services.AddScoped<IArchiveVerifier, ArchiveVerifier>();
        services.AddScoped<IFiscalControlExportService, FiscalControlExportService>();

        // Handler du job système d'ancrage quotidien : le Host le planifie (AddJobHandler + JobScheduler).
        services.AddScoped<DailyAnchoringFanOutHandler>();
    }
}
