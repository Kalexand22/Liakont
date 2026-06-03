namespace Stratum.Common.Infrastructure.BlobStorage;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.BlobStorage;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBlobStore"/> with the local filesystem implementation.
    /// Binds <see cref="BlobStorageOptions"/> from the "BlobStorage" configuration section.
    /// </summary>
    public static IServiceCollection AddStratumBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BlobStorageOptions>(
            configuration.GetSection(BlobStorageOptions.SectionName));
        services.AddSingleton<IBlobStore, LocalBlobStore>();
        return services;
    }
}
