namespace Liakont.Modules.Signature.Infrastructure.Persistence;

using Dapper;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Store Dapper des comptes de signature, tenant-scopé (ADR-0029 §6). Lit/écrit
/// <c>signature.signature_provider_accounts</c> dans la base DU tenant courant (<see cref="IConnectionFactory"/>
/// scopé). Les secrets transitent CHIFFRÉS (jamais en clair — CLAUDE.md n°10) ; le déchiffrement est interne
/// au plug-in (résolveur du Host).
/// </summary>
internal sealed class PostgresSignatureAccountStore : ISignatureAccountStore
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresSignatureAccountStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SignatureProviderAccount?> GetActiveAccountAsync(
        Guid companyId, string providerType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);

        const string sql = """
            SELECT environment       AS Environment,
                   account_identifiers AS AccountIdentifiers,
                   encrypted_api_key   AS EncryptedApiKey,
                   encrypted_webhook_secret AS EncryptedWebhookSecret
            FROM signature.signature_provider_accounts
            WHERE company_id = @CompanyId AND lower(provider_type) = lower(@ProviderType) AND is_active = true
            LIMIT 1
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(
            new CommandDefinition(sql, new { CompanyId = companyId, ProviderType = providerType }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SignatureAccountSettingKeys.Environment] = row.Environment,
            [SignatureAccountSettingKeys.AccountIdentifiers] = row.AccountIdentifiers ?? string.Empty,
        };
        if (!string.IsNullOrEmpty(row.EncryptedApiKey))
        {
            settings[SignatureAccountSettingKeys.EncryptedApiKey] = row.EncryptedApiKey;
        }

        if (!string.IsNullOrEmpty(row.EncryptedWebhookSecret))
        {
            settings[SignatureAccountSettingKeys.EncryptedWebhookSecret] = row.EncryptedWebhookSecret;
        }

        return new SignatureProviderAccount(providerType, companyId.ToString(), row.Environment, settings);
    }

    public async Task UpsertAsync(
        Guid companyId,
        string providerType,
        string environment,
        string accountIdentifiers,
        string encryptedApiKey,
        string encryptedWebhookSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);

        const string sql = """
            INSERT INTO signature.signature_provider_accounts
                (company_id, provider_type, environment, account_identifiers, encrypted_api_key,
                 encrypted_webhook_secret, is_active, created_at, updated_at)
            VALUES
                (@CompanyId, @ProviderType, @Environment, @AccountIdentifiers, @EncryptedApiKey,
                 @EncryptedWebhookSecret, true, now(), now())
            ON CONFLICT (company_id, provider_type) DO UPDATE SET
                environment = EXCLUDED.environment,
                account_identifiers = EXCLUDED.account_identifiers,
                encrypted_api_key = EXCLUDED.encrypted_api_key,
                encrypted_webhook_secret = EXCLUDED.encrypted_webhook_secret,
                is_active = true,
                updated_at = now()
            """;

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                CompanyId = companyId,
                ProviderType = providerType,
                Environment = environment,
                AccountIdentifiers = accountIdentifiers ?? string.Empty,
                EncryptedApiKey = encryptedApiKey,
                EncryptedWebhookSecret = encryptedWebhookSecret,
            },
            cancellationToken: cancellationToken));
    }

    private sealed record AccountRow
    {
        public string Environment { get; init; } = string.Empty;

        public string? AccountIdentifiers { get; init; }

        public string? EncryptedApiKey { get; init; }

        public string? EncryptedWebhookSecret { get; init; }
    }
}
