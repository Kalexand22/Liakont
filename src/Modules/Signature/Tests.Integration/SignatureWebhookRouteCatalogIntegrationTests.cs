namespace Liakont.Modules.Signature.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Signature.Domain.Entities;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Catalogue SYSTÈME de routage par handle opaque (ADR-0029 §2 ; INV-YOUSIGN-3) : enregistre une route et la
/// résout sur la base SYSTÈME ; un handle inconnu renvoie <c>null</c> (404 côté endpoint, jamais une fuite).
/// </summary>
[Collection("SignatureIntegration")]
public sealed class SignatureWebhookRouteCatalogIntegrationTests
{
    private readonly SignatureDatabaseFixture _fixture;

    public SignatureWebhookRouteCatalogIntegrationTests(SignatureDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Registers_and_resolves_an_opaque_handle_to_its_tenant()
    {
        var catalog = new PostgresSignatureWebhookRouteCatalog(SystemFactory());
        var opaque = "opq-" + Guid.NewGuid().ToString("N");
        var company = Guid.NewGuid();

        await catalog.RegisterAsync(new SignatureWebhookRoute
        {
            OpaqueRef = opaque,
            TenantId = "tenant-z",
            CompanyId = company,
            ProviderType = "Yousign",
        });

        var route = await catalog.ResolveAsync(opaque);

        route.Should().NotBeNull();
        route!.TenantId.Should().Be("tenant-z");
        route.CompanyId.Should().Be(company);
        route.ProviderType.Should().Be("Yousign");
    }

    [Fact]
    public async Task Unknown_handle_resolves_to_null()
    {
        var catalog = new PostgresSignatureWebhookRouteCatalog(SystemFactory());

        (await catalog.ResolveAsync("does-not-exist")).Should().BeNull();
    }

    private NpgsqlConnectionFactory SystemFactory() =>
        new(Options.Create(new DatabaseOptions { ConnectionString = _fixture.ConnectionString }));
}
