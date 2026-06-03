namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// Unit tests for <see cref="TenantProvisioningService"/>.
/// Only tests input validation and configuration behavior that does not
/// require a database connection. Integration tests with a real PostgreSQL
/// should use Testcontainers in a separate integration test project.
/// </summary>
public sealed class TenantProvisioningServiceTests
{
    private static TenantProvisioningService CreateService(
        string connectionString = "Host=localhost;Database=stratum",
        string databasePrefix = "stratum_")
    {
        return new TenantProvisioningService(
            Options.Create(new DatabaseOptions { ConnectionString = connectionString }),
            Options.Create(new TenantConnectionOptions { DatabasePrefix = databasePrefix }),
            Options.Create(new MigrationAssembliesOptions()),
            new NoOpKeycloakRealmProvisioner(),
            new NoOpRealmRegistry(),
            Options.Create(new KeycloakAdminOptions { AdminBaseUrl = "http://localhost:8080" }),
            NullLoggerFactory.Instance.CreateLogger<TenantProvisioningService>());
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("acme_corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac me")]
    [InlineData("acme!")]
    [InlineData("'; DROP TABLE tenants; --")]
    [InlineData("")]
    public async Task ProvisionAsync_Should_ReturnFailed_When_TenantIdIsInvalid(string tenantId)
    {
        var service = CreateService();
        var request = new TenantProvisionRequest
        {
            TenantId = tenantId,
            DisplayName = "Test",
            AdminEmail = "test@test.com",
        };

        var result = await service.ProvisionAsync(request);

        Assert.False(result.Success);
        Assert.Contains("Invalid tenant ID format", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionAsync_Should_ReturnFailed_When_DatabaseNameExceedsLimit()
    {
        // prefix "stratum_" (8 chars) + 56-char tenant ID = 64 chars > 63 limit
        var longTenantId = new string('a', 56);
        var service = CreateService();
        var request = new TenantProvisionRequest
        {
            TenantId = longTenantId,
            DisplayName = "Test",
            AdminEmail = "test@test.com",
        };

        var result = await service.ProvisionAsync(request);

        Assert.False(result.Success);
        Assert.Contains("63-character identifier limit", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionAsync_Should_ThrowArgumentNullException_When_RequestIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ProvisionAsync(null!));
    }

    [Fact]
    public async Task ProvisionAsync_Should_ReturnFailed_When_DatabaseNameTooLongWithCustomPrefix()
    {
        // Long prefix (20 chars) + 44-char tenant ID = 64 chars > 63
        var service = CreateService(databasePrefix: "a_very_long_prefix__");
        var request = new TenantProvisionRequest
        {
            TenantId = new string('z', 44),
            DisplayName = "Test",
            AdminEmail = "test@test.com",
        };

        var result = await service.ProvisionAsync(request);

        Assert.False(result.Success);
        Assert.Contains("63-character identifier limit", result.ErrorMessage);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("a")]
    [InlineData("tenant1")]
    [InlineData("a1b2c3")]
    public async Task ProvisionAsync_Should_PassValidation_When_TenantIdIsValid(string tenantId)
    {
        // Valid tenant IDs should pass validation regardless of DB availability.
        // The result may be Success (DB available) or Failed (no DB) — either is fine.
        // What we assert: no validation-level rejection.
        var service = CreateService();
        var request = new TenantProvisionRequest
        {
            TenantId = tenantId,
            DisplayName = "Test",
            AdminEmail = "test@test.com",
        };

        var result = await service.ProvisionAsync(request);

        // Validation errors have specific messages. Whether provisioning succeeded
        // or failed at the DB level, it should not be a validation rejection.
        if (!result.Success)
        {
            Assert.DoesNotContain("Invalid tenant ID format", result.ErrorMessage ?? string.Empty);
            Assert.DoesNotContain("63-character identifier limit", result.ErrorMessage ?? string.Empty);
        }
    }

    [Fact]
    public void ResultCreated_Should_HaveCorrectProperties()
    {
        var result = TenantProvisionResult.Created("stratum_acme", "stratum-acme", "http://localhost:8080/realms/stratum-acme");

        Assert.True(result.Success);
        Assert.False(result.AlreadyProvisioned);
        Assert.Equal("stratum_acme", result.DatabaseName);
        Assert.Equal("stratum-acme", result.RealmName);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResultIdempotent_Should_HaveCorrectProperties()
    {
        var result = TenantProvisionResult.Idempotent("stratum_acme", "stratum-acme", "http://localhost:8080/realms/stratum-acme");

        Assert.True(result.Success);
        Assert.True(result.AlreadyProvisioned);
        Assert.Equal("stratum_acme", result.DatabaseName);
        Assert.Equal("stratum-acme", result.RealmName);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResultFailed_Should_HaveCorrectProperties()
    {
        var result = TenantProvisionResult.Failed("something went wrong");

        Assert.False(result.Success);
        Assert.False(result.AlreadyProvisioned);
        Assert.Null(result.DatabaseName);
        Assert.Equal("something went wrong", result.ErrorMessage);
    }

    // --- DeactivateAsync validation ---
    [Fact]
    public async Task DeactivateAsync_Should_ThrowArgumentException_When_TenantIdIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.DeactivateAsync(null!));
    }

    [Fact]
    public async Task DeactivateAsync_Should_ThrowArgumentException_When_TenantIdIsWhitespace()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.DeactivateAsync("   "));
    }

    // --- DeactivationResult factory tests ---
    [Fact]
    public void DeactivationResultCompleted_Should_HaveCorrectProperties()
    {
        var result = DeactivationResult.Completed();

        Assert.True(result.Success);
        Assert.False(result.TenantNotFound);
        Assert.False(result.AlreadyDeactivated);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void DeactivationResultNotFound_Should_HaveCorrectProperties()
    {
        var result = DeactivationResult.NotFound("acme");

        Assert.False(result.Success);
        Assert.True(result.TenantNotFound);
        Assert.Contains("acme", result.ErrorMessage);
    }

    [Fact]
    public void DeactivationResultAlreadyInactive_Should_HaveCorrectProperties()
    {
        var result = DeactivationResult.AlreadyInactive("old-corp");

        Assert.True(result.Success);
        Assert.True(result.AlreadyDeactivated);
    }

    [Fact]
    public void DeactivationResultFailed_Should_HaveCorrectProperties()
    {
        var result = DeactivationResult.Failed("something went wrong");

        Assert.False(result.Success);
        Assert.Equal("something went wrong", result.ErrorMessage);
    }

    // --- ReprovisionAsync validation ---
    [Fact]
    public async Task ReprovisionAsync_Should_ThrowArgumentException_When_TenantIdIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ReprovisionAsync(null!));
    }

    [Fact]
    public async Task ReprovisionAsync_Should_ThrowArgumentException_When_TenantIdIsWhitespace()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReprovisionAsync("   "));
    }

    // --- ReprovisionResult factory tests ---
    [Fact]
    public void ReprovisionResultCompleted_Should_HaveCorrectProperties()
    {
        var result = ReprovisionResult.Completed("stratum_acme", 5);

        Assert.True(result.Success);
        Assert.Equal("stratum_acme", result.DatabaseName);
        Assert.Equal(5, result.MigrationsApplied);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ReprovisionResultCompleted_Should_AllowZeroMigrations()
    {
        var result = ReprovisionResult.Completed("stratum_acme", 0);

        Assert.True(result.Success);
        Assert.Equal(0, result.MigrationsApplied);
    }

    [Fact]
    public void ReprovisionResultFailed_Should_HaveCorrectProperties()
    {
        var result = ReprovisionResult.Failed("migration error");

        Assert.False(result.Success);
        Assert.False(result.TenantNotFound);
        Assert.Null(result.DatabaseName);
        Assert.Equal(0, result.MigrationsApplied);
        Assert.Equal("migration error", result.ErrorMessage);
    }

    [Fact]
    public void ReprovisionResultNotFound_Should_HaveCorrectProperties()
    {
        var result = ReprovisionResult.NotFound("acme");

        Assert.False(result.Success);
        Assert.True(result.TenantNotFound);
        Assert.Null(result.DatabaseName);
        Assert.Contains("acme", result.ErrorMessage);
    }

    [Fact]
    public void ReprovisionResultDeactivated_Should_HaveCorrectProperties()
    {
        var result = ReprovisionResult.Deactivated("old-corp");

        Assert.False(result.Success);
        Assert.False(result.TenantNotFound);
        Assert.True(result.TenantDeactivated);
        Assert.Contains("deactivated", result.ErrorMessage);
    }

    private sealed class NoOpKeycloakRealmProvisioner : IKeycloakRealmProvisioner
    {
        public Task<KeycloakProvisionResult> ProvisionRealmAsync(
            KeycloakRealmProvisionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(KeycloakProvisionResult.Created(request.RealmName, $"http://localhost:8080/realms/{request.RealmName}", request.ClientSecret));

        public Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddTenantRedirectUriAsync(string primaryRealmName, string tenantSubdomain, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpRealmRegistry : IRealmRegistry
    {
        public bool IsKnownIssuer(string issuer) => false;

        public bool TryGetTenantId(string realmName, out string? tenantId)
        {
            tenantId = null;
            return false;
        }

        public void RegisterRealm(string realmName, string tenantId, string authority)
        {
        }

        public void UnregisterRealm(string realmName, string authority)
        {
        }
    }
}
