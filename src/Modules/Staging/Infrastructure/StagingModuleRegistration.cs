namespace Liakont.Modules.Staging.Infrastructure;

using System;
using System.IO;
using Liakont.Modules.Staging.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Enregistrement DI du module Staging (PIP00, ADR-0014) : magasin de staging du contenu pivot et service
/// de purge subordonnée à la présence WORM. Le store par défaut est <see cref="FileSystemPayloadStagingStore"/>
/// (appliance) ; un plug-in de store (S3, …) le remplacera via <c>Replace</c>, comme le coffre d'archive.
/// La racine du magasin est du paramétrage d'INSTANCE (<c>Staging:Storage:FileSystem:RootPath</c>).
///
/// <see cref="IArchivedDocumentProbe"/> n'est PAS enregistré ici : son implémentation interroge le coffre
/// concret (<c>IArchiveStore</c>) et vit donc au composition root (Host), seul endroit autorisé à référencer
/// un autre module hors Contracts (frontière inter-modules — blueprint §6 ; CLAUDE.md n°14).
/// </summary>
public static class StagingModuleRegistration
{
    /// <summary>Enregistre le magasin de staging FileSystem et le service de purge subordonnée.</summary>
    /// <param name="services">La collection de services.</param>
    /// <param name="configuration">La configuration de l'instance.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddStagingModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FileSystemPayloadStagingStoreOptions>(configuration.GetSection("Staging:Storage:FileSystem"));
        services.PostConfigure<FileSystemPayloadStagingStoreOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                // Repli sous le répertoire de l'instance ; une instance de production configure un volume dédié.
                options.RootPath = Path.Combine(AppContext.BaseDirectory, "staging-store");
            }
        });

        // Store par défaut (FileSystem). Un plug-in de store le remplace via Replace.
        services.TryAddScoped<IPayloadStagingStore, FileSystemPayloadStagingStore>();
        services.AddScoped<IStagingPurgeService, StagingPurgeService>();

        return services;
    }
}
