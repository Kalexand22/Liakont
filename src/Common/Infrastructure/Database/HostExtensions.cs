namespace Stratum.Common.Infrastructure.Database;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public static class HostExtensions
{
    public static IHost MigrateDatabase(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        runner.MigrateUp();
        return host;
    }
}
