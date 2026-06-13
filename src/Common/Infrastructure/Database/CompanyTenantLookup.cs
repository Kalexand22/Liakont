// Liakont addition (RLM02): authoritative company_id → tenant lookup — not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Database;

using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation Dapper de <see cref="ICompanyTenantLookup"/> : lit le registre système
/// <c>outbox.tenants</c> (même table/connexion que <see cref="TenantQueries"/>). Synchrone, requête
/// mono-ligne indexée par la contrainte UNIQUE de V017 — appelée par le résolveur de tenant sur le
/// chemin chaud (ADR-0021 §2c).
/// </summary>
public sealed class CompanyTenantLookup : ICompanyTenantLookup
{
    private const string Sql = "SELECT id FROM outbox.tenants WHERE company_id = @CompanyId";

    private readonly string _connectionString;

    public CompanyTenantLookup(IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionString = databaseOptions.Value.ConnectionString;
    }

    public string? FindTenantId(Guid companyId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        return connection.QuerySingleOrDefault<string?>(Sql, new { CompanyId = companyId });
    }
}
