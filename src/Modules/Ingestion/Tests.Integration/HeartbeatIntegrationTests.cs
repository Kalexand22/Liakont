namespace Liakont.Modules.Ingestion.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Xunit;

[Collection("IngestionIntegration")]
public sealed class HeartbeatIntegrationTests
{
    private readonly IngestionDatabaseFixture _fixture;

    public HeartbeatIntegrationTests(IngestionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Heartbeat_Is_Persisted_And_Updates_Agent_State_With_Safe_Config()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        var issued = await harness.RegisterHandler.Handle(
            new RegisterAgentCommand { Name = "Poste 1" }, CancellationToken.None);

        var request = new HeartbeatRequestDto(
            contractVersion: "1",
            agentVersion: "2.3.1",
            sentAtUtc: new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc),
            lastSuccessfulSyncUtc: null);

        var response = await harness.HeartbeatHandler.Handle(
            new RecordHeartbeatCommand { AgentId = issued.AgentId, Heartbeat = request }, CancellationToken.None);

        // La réponse renvoie une configuration au défaut sûr (registre de versions vide).
        response.Configuration.UpdateRequired.Should().BeFalse();
        response.Configuration.UpdateUrl.Should().BeNull();
        response.Configuration.LatestAgentVersion.Should().BeNull();
        response.Configuration.ExtractionSchedule.Should().BeNull();

        // L'état de l'agent est mis à jour (dernière vue + version).
        var list = await harness.AgentsHandler.Handle(new GetAgentsQuery(), CancellationToken.None);
        list.Should().ContainSingle();
        list[0].LastAgentVersion.Should().Be("2.3.1");
        list[0].LastSeenAtUtc.Should().NotBeNull();

        // L'historique reçoit une entrée (append-only).
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ingestion.agent_heartbeats WHERE agent_id = @Id",
            new { Id = issued.AgentId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetConfiguration_Returns_Safe_Default_When_Version_Registry_Empty()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        var configuration = await harness.ConfigurationHandler.Handle(
            new GetAgentConfigurationQuery { TenantId = harness.TenantId }, CancellationToken.None);

        configuration.UpdateRequired.Should().BeFalse();
        configuration.LatestAgentVersion.Should().BeNull();
        configuration.UpdateUrl.Should().BeNull();
        configuration.VersionManifestSignature.Should().BeNull();
        configuration.ExtractionSchedule.Should().BeNull();
        configuration.ExtractFromUtc.Should().BeNull();
        configuration.ExtractToUtc.Should().BeNull();
    }

    private static string NewTenant() => "tenant-" + Guid.NewGuid().ToString("N")[..8];
}
