namespace Liakont.Modules.TenantSettings.Infrastructure;

using Dapper;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lit les secrets CHIFFRÉS du compte PA actif dans la base du tenant courant (Dapper, via la connexion
/// routée par <see cref="IConnectionFactory"/>). Calqué sur <c>PostgresSignatureAccountStore</c>. Ne
/// déchiffre RIEN (le déchiffrement reste dans le résolveur Host) et ne renvoie que des textes opaques.
/// </summary>
internal sealed class PostgresPaAccountSecretStore : IPaAccountSecretStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresPaAccountSecretStore(IConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<PaAccountSecrets?> GetActiveAsync(Guid companyId, string pluginType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginType);

        // Même sélection que SendTenantJob.ResolveActiveAccountAsync : compte ACTIF de ce type, le plus
        // ancien (created_at ASC) — pour résoudre exactement le compte que l'envoi a retenu.
        const string sql = """
            SELECT environment, account_identifiers, encrypted_api_key,
                   encrypted_client_id, encrypted_client_secret, encrypted_technical_password
            FROM tenantsettings.pa_accounts
            WHERE company_id = @CompanyId
              AND lower(plugin_type) = lower(@PluginType)
              AND is_active = true
            ORDER BY created_at ASC
            LIMIT 1
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { CompanyId = companyId, PluginType = pluginType }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new PaAccountSecrets(
            (PaEnvironment)(int)row.environment,
            (string)row.account_identifiers,
            (string?)row.encrypted_api_key,
            (string?)row.encrypted_client_id,
            (string?)row.encrypted_client_secret,
            (string?)row.encrypted_technical_password);
    }
}
