namespace Stratum.Modules.Identity.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class MigrationVerificationTests
{
    private readonly IdentityDatabaseFixture _fixture;

    public MigrationVerificationTests(IdentityDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("identity", "users")]
    [InlineData("identity", "roles")]
    [InlineData("identity", "user_roles")]
    [InlineData("identity", "grants")]
    [InlineData("identity", "user_preferences")]
    public async Task MigrationShouldCreateExpectedTable(string schema, string table)
    {
        using IDbConnection conn = await _fixture.CreateConnectionFactory().OpenAsync();

        var exists = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @Schema AND table_name = @Table
            )
            """,
            new { Schema = schema, Table = table });

        exists.Should().BeTrue($"table {schema}.{table} should exist after migration");
    }

    [Fact]
    public async Task V010MigrationShouldAddConditionColumnToGrants()
    {
        using IDbConnection conn = await _fixture.CreateConnectionFactory().OpenAsync();

        var exists = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'identity'
                  AND table_name = 'grants'
                  AND column_name = 'condition'
            )
            """);

        exists.Should().BeTrue("V010 should add 'condition' column to identity.grants");
    }
}
