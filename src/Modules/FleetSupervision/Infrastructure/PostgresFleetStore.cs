namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Liakont.Modules.FleetSupervision.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance du parc de flotte (OPS04) dans la base SYSTÈME du central, via <see cref="ISystemConnectionFactory"/>
/// (jamais une connexion tenant). Upsert idempotent par instance qui préserve <c>first_seen_utc</c> et
/// <c>notified_version</c> ; les énumérations sont stockées par leur NOM (robuste à un renumérotage). Mapping
/// des lignes en dynamique + lecture explicite (convention du dépôt, cf. PostgresAlertQueries).
/// </summary>
internal sealed class PostgresFleetStore : IFleetInstanceStore
{
    private readonly ISystemConnectionFactory _connectionFactory;

    public PostgresFleetStore(ISystemConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(FleetInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // ON CONFLICT : on met à jour la télémétrie et last_seen, mais on NE touche PAS first_seen_utc ni
        // notified_version (préservés). À la première insertion, first_seen_utc = last_seen_utc = nowUtc.
        const string sql = """
            INSERT INTO fleet.instances
                (instance_id, display_name, hosting_mode, version, host_health, database_health, keycloak_health,
                 tenant_count, disk_free_bytes, disk_total_bytes, last_backup_utc, contact_email,
                 first_seen_utc, last_seen_utc)
            VALUES
                (@InstanceId, @DisplayName, @HostingMode, @Version, @HostHealth, @DatabaseHealth, @KeycloakHealth,
                 @TenantCount, @DiskFreeBytes, @DiskTotalBytes, @LastBackupUtc, @ContactEmail,
                 @FirstSeenUtc, @LastSeenUtc)
            ON CONFLICT (instance_id) DO UPDATE SET
                display_name = EXCLUDED.display_name,
                hosting_mode = EXCLUDED.hosting_mode,
                version = EXCLUDED.version,
                host_health = EXCLUDED.host_health,
                database_health = EXCLUDED.database_health,
                keycloak_health = EXCLUDED.keycloak_health,
                tenant_count = EXCLUDED.tenant_count,
                disk_free_bytes = EXCLUDED.disk_free_bytes,
                disk_total_bytes = EXCLUDED.disk_total_bytes,
                last_backup_utc = EXCLUDED.last_backup_utc,
                contact_email = EXCLUDED.contact_email,
                last_seen_utc = EXCLUDED.last_seen_utc;
            """;

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                instance.InstanceId,
                instance.DisplayName,
                HostingMode = instance.HostingMode.ToString(),
                instance.Version,
                HostHealth = instance.HostHealth.ToString(),
                DatabaseHealth = instance.DatabaseHealth.ToString(),
                KeycloakHealth = instance.KeycloakHealth.ToString(),
                instance.TenantCount,
                instance.DiskFreeBytes,
                instance.DiskTotalBytes,
                LastBackupUtc = instance.LastSuccessfulBackupUtc,
                instance.ContactEmail,
                instance.FirstSeenUtc,
                instance.LastSeenUtc,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FleetInstanceDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT instance_id, display_name, hosting_mode, version, host_health, database_health, keycloak_health,
                   tenant_count, disk_free_bytes, disk_total_bytes, last_backup_utc, contact_email,
                   first_seen_utc, last_seen_utc
            FROM fleet.instances
            ORDER BY last_seen_utc DESC;
            """;

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<dynamic> rows = await connection.QueryAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<FleetInstanceDto>();
        foreach (dynamic row in rows)
        {
            result.Add(MapToDto(row));
        }

        return result;
    }

    public async Task<IReadOnlyList<FleetNotificationCandidate>> ListNotificationCandidatesAsync(CancellationToken cancellationToken = default)
    {
        // Candidats : instances self-hosted joignables par email. Le filtre « en retard » est laissé à
        // l'appelant (qui connaît la dernière version publiée).
        const string sql = """
            SELECT instance_id, display_name, contact_email, version, notified_version
            FROM fleet.instances
            WHERE hosting_mode = @SelfHosted
              AND contact_email IS NOT NULL
              AND contact_email <> '';
            """;

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<dynamic> rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { SelfHosted = InstanceHostingMode.SelfHosted.ToString() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<FleetNotificationCandidate>();
        foreach (dynamic row in rows)
        {
            result.Add(new FleetNotificationCandidate(
                (string)row.instance_id,
                (string)row.display_name,
                (string?)row.contact_email ?? string.Empty,
                (string)row.version,
                (string?)row.notified_version));
        }

        return result;
    }

    public async Task MarkNotifiedAsync(string instanceId, string notifiedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifiedVersion);

        const string sql = "UPDATE fleet.instances SET notified_version = @NotifiedVersion WHERE instance_id = @InstanceId;";

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { InstanceId = instanceId, NotifiedVersion = notifiedVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static FleetInstanceDto MapToDto(dynamic row) => new()
    {
        InstanceId = (string)row.instance_id,
        DisplayName = (string)row.display_name,
        HostingMode = ParseEnum((string)row.hosting_mode, InstanceHostingMode.Operated),
        Version = (string)row.version,
        HostHealth = ParseEnum((string)row.host_health, InstanceHealthStatus.Unknown),
        DatabaseHealth = ParseEnum((string)row.database_health, InstanceHealthStatus.Unknown),
        KeycloakHealth = ParseEnum((string)row.keycloak_health, InstanceHealthStatus.Unknown),
        TenantCount = (int)row.tenant_count,
        DiskFreeBytes = (long)row.disk_free_bytes,
        DiskTotalBytes = (long)row.disk_total_bytes,
        LastSuccessfulBackupUtc = FleetRowReader.ToNullableDateTimeOffset((object?)row.last_backup_utc),
        ContactEmail = (string?)row.contact_email,
        FirstSeenUtc = FleetRowReader.ToDateTimeOffset((object)row.first_seen_utc),
        LastSeenUtc = FleetRowReader.ToDateTimeOffset((object)row.last_seen_utc),
    };

    private static TEnum ParseEnum<TEnum>(string? raw, TEnum fallback)
        where TEnum : struct =>
        Enum.TryParse(raw, ignoreCase: false, out TEnum value) ? value : fallback;

    private Task<IDbConnection> OpenAsync(CancellationToken cancellationToken) =>
        _connectionFactory.OpenAsync(cancellationToken);
}
