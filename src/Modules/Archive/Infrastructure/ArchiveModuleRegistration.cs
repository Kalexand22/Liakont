namespace Liakont.Modules.Archive.Infrastructure;

using System.IO;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du module Archive (TRK05) : coffre WORM, chaîne de hashes, addenda chaînés, export
/// d'intégrité. N'apporte AUCUNE migration : il alimente <c>documents.archive_entries</c> (table créée par
/// le module Documents, TRK01). Le store par défaut est <see cref="FileSystemArchiveStore"/> (appliance) ;
/// une instance peut le remplacer par un backend S3-compatible (plug-in <c>Stores.S3</c>, ADR-0009) sans
/// toucher au module — le choix est une configuration d'instance.
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

        return services;
    }
}
