namespace Liakont.Modules.Signature.Infrastructure.Persistence;

using Dapper;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Catalogue SYSTÈME des routes de webhook (ADR-0029 §2 ; INV-YOUSIGN-3). Interroge
/// <c>signature.signature_webhook_routes</c> sur la base SYSTÈME (<see cref="ISystemConnectionFactory"/>),
/// AVANT toute ouverture de scope tenant — pur aiguillage <c>{opaque_ref} → tenant</c>, jamais un lookup
/// métier cross-tenant (modèle <c>CompanyTenantLookup</c>).
/// </summary>
internal sealed class PostgresSignatureWebhookRouteCatalog : ISignatureWebhookRouteCatalog
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresSignatureWebhookRouteCatalog(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<SignatureWebhookRoute?> ResolveAsync(string opaqueRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(opaqueRef))
        {
            return null;
        }

        const string sql = """
            SELECT opaque_ref    AS OpaqueRef,
                   tenant_id     AS TenantId,
                   company_id    AS CompanyId,
                   provider_type AS ProviderType
            FROM signature.signature_webhook_routes
            WHERE opaque_ref = @OpaqueRef
            LIMIT 1
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RouteRow>(
            new CommandDefinition(sql, new { OpaqueRef = opaqueRef }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new SignatureWebhookRoute
        {
            OpaqueRef = row.OpaqueRef,
            TenantId = row.TenantId,
            CompanyId = row.CompanyId,
            ProviderType = row.ProviderType,
        };
    }

    public async Task RegisterAsync(SignatureWebhookRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        const string sql = """
            INSERT INTO signature.signature_webhook_routes (opaque_ref, tenant_id, company_id, provider_type, created_at)
            VALUES (@OpaqueRef, @TenantId, @CompanyId, @ProviderType, now())
            ON CONFLICT (opaque_ref) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id,
                company_id = EXCLUDED.company_id,
                provider_type = EXCLUDED.provider_type
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { route.OpaqueRef, route.TenantId, route.CompanyId, route.ProviderType },
            cancellationToken: cancellationToken));
    }

    private sealed record RouteRow
    {
        public string OpaqueRef { get; init; } = string.Empty;

        public string TenantId { get; init; } = string.Empty;

        public Guid CompanyId { get; init; }

        public string ProviderType { get; init; } = string.Empty;
    }
}
