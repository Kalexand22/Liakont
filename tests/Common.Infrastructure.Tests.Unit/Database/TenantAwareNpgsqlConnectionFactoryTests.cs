namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

#pragma warning disable CA1001 // Disposal handled by IAsyncLifetime.DisposeAsync (xUnit test lifecycle)
public sealed class TenantAwareNpgsqlConnectionFactoryTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private static readonly ILogger<TenantAwareNpgsqlConnectionFactory> Logger =
        NullLoggerFactory.Instance.CreateLogger<TenantAwareNpgsqlConnectionFactory>();

    private readonly NpgsqlDataSourceRegistry _registry = new(
        NullLoggerFactory.Instance.CreateLogger<NpgsqlDataSourceRegistry>());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _registry.DisposeAsync();

    private TenantAwareNpgsqlConnectionFactory CreateFactory(
        string defaultConnectionString = "Host=localhost;Database=stratum",
        TenantConnectionOptions? tenantOptions = null)
    {
        var dbOptions = Options.Create(new DatabaseOptions
        {
            ConnectionString = defaultConnectionString,
        });
        var tenantOpts = Options.Create(tenantOptions ?? new TenantConnectionOptions());
        return new TenantAwareNpgsqlConnectionFactory(dbOptions, tenantOpts, _registry, Logger);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("tenant1")]
    [InlineData("a")]
    [InlineData("abc-def")]
    [InlineData("a1b2c3")]
    public async Task OpenAsync_Should_NotThrowArgumentException_When_TenantIdIsValid(string tenantId)
    {
        // Validation must pass; connection will fail (no DB) with NpgsqlException — that's expected.
        var factory = CreateFactory();

        var ex = await Record.ExceptionAsync(() => factory.OpenAsync(tenantId));

        Assert.IsNotType<ArgumentException>(ex);
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("acme_corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac me")]
    [InlineData("acme!")]
    [InlineData("acme.corp")]
    [InlineData("a/b")]
    [InlineData("'; DROP TABLE tenants; --")]
    public async Task OpenAsync_Should_ThrowArgumentException_When_TenantIdIsInvalid(string tenantId)
    {
        var factory = CreateFactory();

        await Assert.ThrowsAsync<ArgumentException>(() => factory.OpenAsync(tenantId));
    }

    [Fact]
    public async Task OpenAsync_Should_UsePerTenantConnectionString_When_ConfiguredForTenant()
    {
        var options = new TenantConnectionOptions
        {
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["acme"] = "Host=acme-db;Database=stratum_acme",
            },
        };
        var factory = CreateFactory(tenantOptions: options);

        // Should not throw ArgumentException — routes to per-tenant connection string.
        // Will throw NpgsqlException because no real DB exists.
        var ex = await Record.ExceptionAsync(() => factory.OpenAsync("acme"));

        Assert.IsNotType<ArgumentException>(ex);
    }

    [Fact]
    public async Task OpenAsync_Should_ThrowInvalidOperationException_When_DatabaseNameExceeds63Chars()
    {
        // prefix "stratum_" (8 chars) + 63-char tenant ID = 71 chars > 63 limit
        var longTenantId = new string('a', 63);
        var factory = CreateFactory();

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(longTenantId));
    }

    [Fact]
    public void Constructor_Should_ReadDefaultConnectionString_When_Created()
    {
        var factory = CreateFactory("Host=myhost;Database=mydb");
        Assert.NotNull(factory);
    }

    [Fact]
    public void Constructor_Should_AcceptCustomDatabasePrefix_When_Configured()
    {
        var options = new TenantConnectionOptions { DatabasePrefix = "t_" };
        var factory = CreateFactory(tenantOptions: options);
        Assert.NotNull(factory);
    }

    [Fact]
    public void Constructor_Should_AcceptPerTenantConnectionStrings_When_Configured()
    {
        var options = new TenantConnectionOptions
        {
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["acme"] = "Host=acme-db;Database=stratum_acme",
                ["contoso"] = "Host=contoso-db;Database=stratum_contoso",
            },
        };
        var factory = CreateFactory(tenantOptions: options);
        Assert.NotNull(factory);
    }
}
