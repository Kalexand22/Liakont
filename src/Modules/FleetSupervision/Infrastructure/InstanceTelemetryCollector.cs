namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Collecte la télémétrie TECHNIQUE locale d'une instance (OPS04) : version de plateforme
/// (<c>AssemblyInformationalVersion</c>), santé (Host via <see cref="HealthCheckService"/>, PostgreSQL via la
/// sonde « postgres », Keycloak via une sonde HTTP optionnelle), NOMBRE de tenants actifs, espace disque, et
/// dernière sauvegarde réussie (date du fichier marqueur). N'expose JAMAIS de donnée métier d'un éditeur :
/// seul le nombre de tenants est rapporté, jamais leurs noms ni leurs données.
/// </summary>
internal sealed partial class InstanceTelemetryCollector : IInstanceTelemetryCollector
{
    private const string DatabaseHealthCheckName = "postgres";

    private readonly HealthCheckService _healthChecks;
    private readonly ITenantQueries _tenantQueries;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FleetSupervisionOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<InstanceTelemetryCollector> _logger;

    public InstanceTelemetryCollector(
        HealthCheckService healthChecks,
        ITenantQueries tenantQueries,
        IHttpClientFactory httpClientFactory,
        IOptions<FleetSupervisionOptions> options,
        TimeProvider clock,
        ILogger<InstanceTelemetryCollector> logger)
    {
        _healthChecks = healthChecks;
        _tenantQueries = tenantQueries;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<InstanceHeartbeatReport> CollectAsync(CancellationToken cancellationToken = default)
    {
        FleetReportingOptions reporting = _options.Value.Reporting;

        HealthReport health = await _healthChecks.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        InstanceHealthStatus hostHealth = MapStatus(health.Status);
        InstanceHealthStatus databaseHealth = health.Entries.TryGetValue(DatabaseHealthCheckName, out HealthReportEntry dbEntry)
            ? MapStatus(dbEntry.Status)
            : InstanceHealthStatus.Unknown;
        InstanceHealthStatus keycloakHealth = await ProbeKeycloakAsync(reporting.KeycloakProbeUrl, cancellationToken).ConfigureAwait(false);

        int tenantCount = await CountActiveTenantsAsync(cancellationToken).ConfigureAwait(false);
        (long diskFree, long diskTotal) = ReadDiskSpace(reporting.DataPath);
        DateTimeOffset? lastBackup = ReadLastBackupUtc(reporting.BackupMarkerPath);

        string displayName = string.IsNullOrWhiteSpace(reporting.DisplayName) ? reporting.InstanceId : reporting.DisplayName;

        return new InstanceHeartbeatReport
        {
            InstanceId = reporting.InstanceId,
            DisplayName = displayName,
            HostingMode = reporting.HostingMode,
            Version = ReadPlatformVersion(),
            HostHealth = hostHealth,
            DatabaseHealth = databaseHealth,
            KeycloakHealth = keycloakHealth,
            TenantCount = tenantCount,
            DiskFreeBytes = diskFree,
            DiskTotalBytes = diskTotal,
            LastSuccessfulBackupUtc = lastBackup,
            ContactEmail = reporting.ContactEmail,
            SentAtUtc = _clock.GetUtcNow(),
        };
    }

    private static InstanceHealthStatus MapStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => InstanceHealthStatus.Healthy,
        HealthStatus.Degraded => InstanceHealthStatus.Degraded,
        HealthStatus.Unhealthy => InstanceHealthStatus.Unhealthy,
        _ => InstanceHealthStatus.Unknown,
    };

    private static string ReadPlatformVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;

    private static (long Free, long Total) ReadDiskSpace(string? dataPath)
    {
        try
        {
            string path = string.IsNullOrWhiteSpace(dataPath) ? Directory.GetCurrentDirectory() : dataPath;
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return (0, 0);
            }

            var drive = new DriveInfo(root);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    private static DateTimeOffset? ReadLastBackupUtc(string? backupMarkerPath)
    {
        if (string.IsNullOrWhiteSpace(backupMarkerPath) || !File.Exists(backupMarkerPath))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(backupMarkerPath), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sonde Keycloak ({ProbeUrl}) injoignable : {Error}.")]
    private static partial void LogKeycloakProbeFailed(ILogger logger, string probeUrl, string error);

    private async Task<int> CountActiveTenantsAsync(CancellationToken cancellationToken)
    {
        // Uniquement le NOMBRE de tenants actifs — jamais leurs identités (cloisonnement éditeur, OPS04).
        var tenants = await _tenantQueries.ListAsync(cancellationToken).ConfigureAwait(false);
        return tenants.Count;
    }

    private async Task<InstanceHealthStatus> ProbeKeycloakAsync(string? probeUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(probeUrl))
        {
            return InstanceHealthStatus.Unknown;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(FleetHttpClients.KeycloakProbe);
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? InstanceHealthStatus.Healthy : InstanceHealthStatus.Unhealthy;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            LogKeycloakProbeFailed(_logger, probeUrl, ex.Message);
            return InstanceHealthStatus.Unhealthy;
        }
    }
}
