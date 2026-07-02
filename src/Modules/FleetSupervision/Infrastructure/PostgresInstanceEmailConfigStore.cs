namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.FleetSupervision.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance de la configuration d'envoi d'emails d'INSTANCE (ADR-0039) dans la base SYSTÈME, via
/// <see cref="ISystemConnectionFactory"/> (jamais une connexion tenant ; précédent <see cref="PostgresFleetStore"/>).
/// Ligne SINGLETON (<c>fleet.instance_email_config</c>, PK + CHECK sur <c>singleton_id = true</c>) mise à jour par
/// upsert idempotent. Le magasin ne manipule que du <em>ciphertext</em> (secrets déjà chiffrés par le Host) et des
/// non-secrets en clair — jamais de secret en clair, jamais d'appel à <c>ISecretProtector</c> (frontière n°6/14).
/// L'énumération <see cref="EmailProviderKind"/> est stockée/relue par son NOM (robuste à un renumérotage).
/// </summary>
internal sealed class PostgresInstanceEmailConfigStore : IInstanceEmailConfigStore
{
    private readonly ISystemConnectionFactory _connectionFactory;

    public PostgresInstanceEmailConfigStore(ISystemConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<InstanceEmailConfig?> GetAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT kind, host, port, use_starttls, from_address, from_name, username,
                   encrypted_smtp_password, oauth_client_id, oauth_tenant_id,
                   encrypted_oauth_client_secret, encrypted_oauth_refresh_token, enabled
            FROM fleet.instance_email_config
            WHERE singleton_id = true;
            """;

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new InstanceEmailConfig
        {
            Kind = Enum.Parse<EmailProviderKind>((string)row.kind),
            Host = (string)row.host,
            Port = (int)row.port,
            UseStartTls = (bool)row.use_starttls,
            FromAddress = (string)row.from_address,
            FromName = (string)row.from_name,
            Username = (string)row.username,
            EncryptedSmtpPassword = row.encrypted_smtp_password as string,
            OAuthClientId = row.oauth_client_id as string,
            OAuthTenantId = row.oauth_tenant_id as string,
            EncryptedOAuthClientSecret = row.encrypted_oauth_client_secret as string,
            EncryptedOAuthRefreshToken = row.encrypted_oauth_refresh_token as string,
            Enabled = (bool)row.enabled,
        };
    }

    public async Task UpsertAsync(InstanceEmailConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        const string sql = """
            INSERT INTO fleet.instance_email_config
                (singleton_id, kind, host, port, use_starttls, from_address, from_name, username,
                 encrypted_smtp_password, oauth_client_id, oauth_tenant_id,
                 encrypted_oauth_client_secret, encrypted_oauth_refresh_token, enabled, updated_at_utc)
            VALUES
                (true, @Kind, @Host, @Port, @UseStartTls, @FromAddress, @FromName, @Username,
                 @EncryptedSmtpPassword, @OAuthClientId, @OAuthTenantId,
                 @EncryptedOAuthClientSecret, @EncryptedOAuthRefreshToken, @Enabled, now())
            ON CONFLICT (singleton_id) DO UPDATE SET
                kind = EXCLUDED.kind,
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                use_starttls = EXCLUDED.use_starttls,
                from_address = EXCLUDED.from_address,
                from_name = EXCLUDED.from_name,
                username = EXCLUDED.username,
                encrypted_smtp_password = EXCLUDED.encrypted_smtp_password,
                oauth_client_id = EXCLUDED.oauth_client_id,
                oauth_tenant_id = EXCLUDED.oauth_tenant_id,
                encrypted_oauth_client_secret = EXCLUDED.encrypted_oauth_client_secret,
                encrypted_oauth_refresh_token = EXCLUDED.encrypted_oauth_refresh_token,
                enabled = EXCLUDED.enabled,
                updated_at_utc = now();
            """;

        using IDbConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Kind = config.Kind.ToString(),
                config.Host,
                config.Port,
                config.UseStartTls,
                config.FromAddress,
                config.FromName,
                config.Username,
                config.EncryptedSmtpPassword,
                config.OAuthClientId,
                config.OAuthTenantId,
                config.EncryptedOAuthClientSecret,
                config.EncryptedOAuthRefreshToken,
                config.Enabled,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private Task<IDbConnection> OpenAsync(CancellationToken cancellationToken) =>
        _connectionFactory.OpenAsync(cancellationToken);
}
