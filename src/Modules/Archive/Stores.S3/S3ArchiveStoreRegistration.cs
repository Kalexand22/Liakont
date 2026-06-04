namespace Liakont.Modules.Archive.Stores.S3;

using Amazon.Runtime;
using Amazon.S3;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>
/// Branche le coffre S3-compatible comme backend du module Archive (ADR-0009), à la place du
/// <c>FileSystemArchiveStore</c> par défaut. Appelé par le composition root d'une instance QUI A CHOISI S3
/// (configuration d'instance) ; jamais par le module lui-même — l'intégrité produit (chaîne de hashes)
/// est identique quel que soit le backend.
/// </summary>
public static class S3ArchiveStoreRegistration
{
    public static IServiceCollection AddS3ArchiveStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<S3ArchiveStoreOptions>(configuration.GetSection("Archive:Storage:S3"));

        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            S3ArchiveStoreOptions options = serviceProvider.GetRequiredService<IOptions<S3ArchiveStoreOptions>>().Value;
            var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
            }

            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.AuthenticationRegion = options.Region;
            }

            var credentials = new BasicAWSCredentials(options.AccessKeyId ?? string.Empty, options.SecretAccessKey ?? string.Empty);
            return new AmazonS3Client(credentials, config);
        });

        services.AddScoped<IS3BlobClient, AwsS3BlobClient>();

        // Remplace le store par défaut (FileSystem) du module par le backend S3 — choix d'instance.
        services.Replace(ServiceDescriptor.Scoped<IArchiveStore, S3ArchiveStore>());
        return services;
    }
}
