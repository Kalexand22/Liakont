namespace Liakont.Host.Staging;

using System;
using System.IO;
using Liakont.Modules.Staging.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Câblage du composition root (Host) pour l'emplacement du magasin de staging (PIP00, ADR-0014).
/// La racine est du PARAMÉTRAGE d'INSTANCE (<c>Staging:Storage:FileSystem:RootPath</c> ; une prod configure
/// un volume dédié, distinct du coffre WORM). À défaut, on impose un repli STABLE — JAMAIS sous l'arbre de
/// build (FIX07b) : le repli du module est <see cref="AppContext.BaseDirectory"/> (c.-à-d. <c>bin/</c> en dev),
/// effacé au rebuild/redéploiement → contenu stagé perdu → documents zombies. On le remplace par
/// <c>App_Data</c> (hors <c>bin/</c>), comme les PDF reçus (<c>IngestionStorageOptions.PdfRootPath</c>).
/// </summary>
internal static class StagingHostRegistration
{
    /// <summary>
    /// Impose le repli STABLE de la racine de staging (hors arbre de build) quand aucune valeur d'instance n'est
    /// configurée. Via <see cref="OptionsServiceCollectionExtensions.Configure{TOptions}(IServiceCollection, Action{TOptions})"/>
    /// (un <see cref="IConfigureOptions{TOptions}"/>) : le framework exécute TOUS les <see cref="IConfigureOptions{TOptions}"/>
    /// AVANT tout <see cref="IPostConfigureOptions{TOptions}"/>, donc ce défaut s'applique avant — et l'emporte sur — le
    /// repli <see cref="AppContext.BaseDirectory"/> du module (un <see cref="IPostConfigureOptions{TOptions}"/>), qui ne
    /// s'appliquera plus (racine déjà renseignée). Une valeur explicitement configurée (liée plus tôt par le
    /// <see cref="IConfigureOptions{TOptions}"/> du module) l'emporte sur les deux.
    /// </summary>
    /// <param name="services">La collection de services.</param>
    /// <param name="contentRootPath">Le content root de l'instance (base du repli stable, hors <c>bin/</c>).</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddStableStagingRoot(this IServiceCollection services, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        services.Configure<FileSystemPayloadStagingStoreOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                options.RootPath = Path.Combine(contentRootPath, "App_Data", "staging-store");
            }
        });

        return services;
    }
}
