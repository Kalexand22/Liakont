namespace Liakont.Modules.Ingestion.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

[Collection("IngestionIntegration")]
public sealed class AgentLifecycleIntegrationTests
{
    private readonly IngestionDatabaseFixture _fixture;

    public AgentLifecycleIntegrationTests(IngestionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Register_Issues_Key_And_Stores_Only_Prefix_And_Hash()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste de vente 1" }, CancellationToken.None);

        issued.FullKey.Should().StartWith(issued.KeyPrefix + ".");

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var row = await conn.QuerySingleAsync(
            "SELECT key_prefix, key_hash, tenant_id FROM ingestion.agents WHERE id = @Id",
            new { Id = issued.AgentId });

        ((string)row.key_prefix).Should().Be(issued.KeyPrefix);
        ((string)row.key_hash).Should().NotBeNullOrEmpty();
        ((string)row.key_hash).Should().NotBe(issued.FullKey, "seule l'empreinte est stockée, jamais le clair.");
        ((string)row.key_hash).Should().NotContain(issued.FullKey);
        ((string)row.tenant_id).Should().Be(harness.TenantId);
    }

    [Fact]
    public async Task Authenticate_Valid_Key_Resolves_Agent_And_Tenant()
    {
        var tenant = NewTenant();
        var harness = new IngestionHarness(_fixture, tenant);
        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        var result = await harness.Authenticator.AuthenticateAsync(issued.FullKey);

        result.Outcome.Should().Be(AgentAuthenticationOutcome.Authenticated);
        result.Identity.Should().NotBeNull();
        result.Identity!.AgentId.Should().Be(issued.AgentId);
        result.Identity.TenantId.Should().Be(tenant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("agt_inconnu.secret")]
    public async Task Authenticate_Invalid_Key_Returns_InvalidKey(string? key)
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.RegisterHandler.Handle(new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        var result = await harness.Authenticator.AuthenticateAsync(key);

        result.Outcome.Should().Be(AgentAuthenticationOutcome.InvalidKey);
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task Authenticate_Known_Prefix_Wrong_Secret_Returns_InvalidKey()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        var result = await harness.Authenticator.AuthenticateAsync(issued.KeyPrefix + ".mauvais_secret");

        result.Outcome.Should().Be(AgentAuthenticationOutcome.InvalidKey);
    }

    [Fact]
    public async Task Authenticate_Revoked_Key_Returns_Revoked()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        await harness.RevokeHandler.Handle(new RevokeAgentCommand { AgentId = issued.AgentId }, CancellationToken.None);

        var result = await harness.Authenticator.AuthenticateAsync(issued.FullKey);

        result.Outcome.Should().Be(AgentAuthenticationOutcome.Revoked);
    }

    [Fact]
    public async Task Rotate_Invalidates_Old_Key_And_Activates_New_One()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        var rotated = await harness.RotateHandler.Handle(
            new RotateAgentKeyCommand { AgentId = issued.AgentId }, CancellationToken.None);

        rotated.AgentId.Should().Be(issued.AgentId);
        rotated.FullKey.Should().NotBe(issued.FullKey);

        (await harness.Authenticator.AuthenticateAsync(issued.FullKey)).Outcome
            .Should().Be(AgentAuthenticationOutcome.InvalidKey);
        (await harness.Authenticator.AuthenticateAsync(rotated.FullKey)).Outcome
            .Should().Be(AgentAuthenticationOutcome.Authenticated);
    }

    [Fact]
    public async Task Revoke_Agent_Of_Another_Tenant_Throws_NotFound()
    {
        var tenantA = new IngestionHarness(_fixture, NewTenant());
        var issued = await tenantA.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Agent A" }, CancellationToken.None);

        var tenantB = new IngestionHarness(_fixture, NewTenant());
        var act = () => tenantB.RevokeHandler.Handle(
            new RevokeAgentCommand { AgentId = issued.AgentId }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>(
            "un opérateur ne peut révoquer que les agents de son propre tenant.");
    }

    [Fact]
    public async Task List_Is_Scoped_To_Tenant_And_Hides_Keys()
    {
        var tenantA = new IngestionHarness(_fixture, NewTenant());
        var issuedA = await tenantA.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Agent A" }, CancellationToken.None);

        var tenantB = new IngestionHarness(_fixture, NewTenant());
        await tenantB.RegisterHandler.Handle(new RegisterAgentCommand { Name = "Agent B" }, CancellationToken.None);

        var listA = await tenantA.AgentsHandler.Handle(new GetAgentsQuery(), CancellationToken.None);

        listA.Should().ContainSingle();
        listA[0].Id.Should().Be(issuedA.AgentId);
        listA[0].Name.Should().Be("Agent A");
        listA[0].KeyPrefix.Should().Be(issuedA.KeyPrefix);
        listA[0].IsRevoked.Should().BeFalse();
    }

    private static string NewTenant() => "tenant-" + Guid.NewGuid().ToString("N")[..8];
}
