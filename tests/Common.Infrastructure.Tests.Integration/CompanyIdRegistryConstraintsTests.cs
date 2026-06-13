namespace Stratum.Common.Infrastructure.Tests.Integration;

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Testing;
using Xunit;

/// <summary>
/// Vérifie les contraintes STRUCTURELLES posées par la migration V017 sur <c>outbox.tenants.company_id</c>
/// (ADR-0021 §2c / RLM02) et le lookup autoritaire <see cref="CompanyTenantLookup"/> : la base est
/// migrée jusqu'à V017 inclus par <see cref="DatabaseFixture"/>.
/// </summary>
public sealed class CompanyIdRegistryConstraintsTests : IClassFixture<DatabaseFixture>
{
    private const string UniqueViolation = "23505";
    private const string NotNullViolation = "23502";

    private readonly DatabaseFixture _fixture;

    public CompanyIdRegistryConstraintsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompanyId_Column_Is_NotNull_After_V017()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();

        var isNullable = await connection.QuerySingleAsync<string>(
            """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema = 'outbox' AND table_name = 'tenants' AND column_name = 'company_id'
            """);

        isNullable.Should().Be("NO", "V017 impose company_id NOT NULL (un tenant NULL casserait la résolution)");
    }

    [Fact]
    public async Task Inserting_Duplicate_CompanyId_Is_Rejected_By_Unique_Constraint()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        var shared = Guid.NewGuid();

        await InsertTenantAsync(connection, "dup-a-" + Guid.NewGuid().ToString("N"), shared);

        var act = async () => await InsertTenantAsync(connection, "dup-b-" + Guid.NewGuid().ToString("N"), shared);

        (await act.Should().ThrowAsync<PostgresException>("INV-0021-2c : un doublon de company_id est rejeté"))
            .Which.SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task Inserting_Null_CompanyId_Is_Rejected_By_NotNull_Constraint()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();

        var act = async () => await InsertTenantAsync(connection, "null-" + Guid.NewGuid().ToString("N"), companyId: null);

        (await act.Should().ThrowAsync<PostgresException>("V017 interdit un company_id NULL"))
            .Which.SqlState.Should().Be(NotNullViolation);
    }

    [Fact]
    public async Task CompanyTenantLookup_Resolves_Tenant_By_CompanyId()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        var companyId = Guid.NewGuid();
        var tenantId = "lookup-" + Guid.NewGuid().ToString("N");
        await InsertTenantAsync(connection, tenantId, companyId);

        var lookup = new CompanyTenantLookup(
            Options.Create(new DatabaseOptions { ConnectionString = _fixture.ConnectionString }));

        lookup.FindTenantId(companyId).Should().Be(tenantId);
        lookup.FindTenantId(Guid.NewGuid()).Should().BeNull("un company_id inconnu ne résout aucun tenant");
    }

    private static async Task InsertTenantAsync(IDbConnection connection, string id, Guid? companyId)
    {
        // database_name et realm_name sont UNIQUE : dérivés de l'id pour éviter les collisions inter-tests.
        await connection.ExecuteAsync(
            """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret, company_id)
            VALUES (@id, @id, 'dev@liakont.local', @databaseName, @realmName, NULL, @companyId)
            """,
            new
            {
                id,
                databaseName = "db_" + id,
                realmName = "realm_" + id,
                companyId,
            });
    }
}
