namespace Liakont.Agent.Core.Tests.Transport;

using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Transport;
using Xunit;

/// <summary>
/// Client HTTP du heartbeat et de la configuration (F12 §3.2, AGT03), testé contre un gestionnaire
/// HTTP mocké : en-têtes d'authentification, corps de heartbeat (télémétrie F12 §2.5, nuls omis),
/// désérialisation des réponses émises en camelCase par la plateforme (System.Text.Json) côté agent
/// (Newtonsoft, insensible à la casse), et traduction des codes d'erreur — sans jamais lever.
/// </summary>
public class HttpPlatformClientHeartbeatTests
{
    [Fact]
    public void Heartbeat_sends_api_key_and_contract_version_headers()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK, "{\"serverTimeUtc\":\"2026-06-05T08:00:01Z\",\"configuration\":{}}"));
        HttpPlatformClient client = CreateClient(handler);

        client.SendHeartbeat(SampleHeartbeat());

        HttpRequestMessage request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.Should().Contain("api/agent/v1/heartbeat");
        request.Headers.GetValues(AgentApiHeaders.AgentKey).Should().ContainSingle().Which.Should().Be("prefix.secret");
        request.Headers.GetValues(AgentApiHeaders.ContractVersion).Should().ContainSingle().Which.Should().Be(AgentContractVersion.ContractVersion);
    }

    [Fact]
    public void Heartbeat_body_carries_telemetry_and_omits_null_fields()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK, "{\"serverTimeUtc\":\"2026-06-05T08:00:01Z\",\"configuration\":{}}"));
        HttpPlatformClient client = CreateClient(handler);

        client.SendHeartbeat(SampleHeartbeat());

        string body = handler.RequestBodies[0];
        body.Should().Contain("\"AgentVersion\":\"1.2.3\"");
        body.Should().Contain("\"ServiceState\":\"Running\"");
        body.Should().Contain("\"PushQueueDepth\":4");
        body.Should().Contain("\"PushQueueErrorCount\":1");
        body.Should().Contain("\"DiskFreeBytes\":123456789");

        // Champs nuls omis (un agent sans dernier run connu n'envoie pas de bruit).
        body.Should().NotContain("LastRunStartedUtc");
        body.Should().NotContain("LastError");
    }

    [Fact]
    public void Heartbeat_200_parses_server_time_and_configuration_in_camelcase()
    {
        // La plateforme (System.Text.Json, défauts web) sérialise en camelCase ; l'agent (Newtonsoft)
        // doit désérialiser sans accroc — preuve de compatibilité cross-sérialiseur de l'enveloppe.
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"serverTimeUtc\":\"2026-06-05T08:00:01Z\",\"configuration\":{\"extractionSchedule\":\"0 2 * * *\",\"extractFromUtc\":\"2026-06-01T00:00:00Z\",\"updateRequired\":true,\"updateUrl\":\"https://maj.test/agent.msi\"}}"));
        HttpPlatformClient client = CreateClient(handler);

        HeartbeatOutcome outcome = client.SendHeartbeat(SampleHeartbeat());

        outcome.Kind.Should().Be(PlatformResponseKind.Ok);
        outcome.ServerTimeUtc.Should().Be(new DateTime(2026, 6, 5, 8, 0, 1, DateTimeKind.Utc));
        outcome.Configuration.Should().NotBeNull();
        outcome.Configuration!.ExtractionSchedule.Should().Be("0 2 * * *");
        outcome.Configuration.ExtractFromUtc.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        outcome.Configuration.UpdateRequired.Should().BeTrue();
        outcome.Configuration.UpdateUrl.Should().Be("https://maj.test/agent.msi");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PlatformResponseKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PlatformResponseKind.Unauthorized)]
    [InlineData((HttpStatusCode)426, PlatformResponseKind.UpgradeRequired)]
    [InlineData((HttpStatusCode)429, PlatformResponseKind.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PlatformResponseKind.Throttled)]
    public void Heartbeat_maps_http_status_to_response_kind(HttpStatusCode statusCode, PlatformResponseKind expected)
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Status(statusCode));
        HttpPlatformClient client = CreateClient(handler);

        HeartbeatOutcome outcome = client.SendHeartbeat(SampleHeartbeat());

        outcome.Kind.Should().Be(expected);
        outcome.Configuration.Should().BeNull();
    }

    [Fact]
    public void Heartbeat_network_failure_maps_to_transport_error_without_throwing()
    {
        var handler = new StubHttpMessageHandler((req, body) => throw new HttpRequestException("réseau coupé"));
        HttpPlatformClient client = CreateClient(handler);

        HeartbeatOutcome outcome = client.SendHeartbeat(SampleHeartbeat());

        outcome.Kind.Should().Be(PlatformResponseKind.TransportError);
        outcome.Configuration.Should().BeNull();
    }

    [Fact]
    public void Configuration_get_uses_get_verb_and_parses_camelcase()
    {
        var handler = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK, "{\"extractionSchedule\":\"0 3 * * *\",\"updateRequired\":false}"));
        HttpPlatformClient client = CreateClient(handler);

        ConfigurationOutcome outcome = client.GetConfiguration();

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsoluteUri.Should().Contain("api/agent/v1/configuration");
        outcome.Kind.Should().Be(PlatformResponseKind.Ok);
        outcome.Configuration!.ExtractionSchedule.Should().Be("0 3 * * *");
        outcome.Configuration.UpdateRequired.Should().BeFalse();
    }

    [Fact]
    public void Configuration_unreachable_maps_to_transport_error_without_throwing()
    {
        var handler = new StubHttpMessageHandler((req, body) => throw new HttpRequestException("DNS"));
        HttpPlatformClient client = CreateClient(handler);

        ConfigurationOutcome outcome = client.GetConfiguration();

        outcome.Kind.Should().Be(PlatformResponseKind.TransportError);
        outcome.Configuration.Should().BeNull();
    }

    private static HeartbeatRequestDto SampleHeartbeat() => new HeartbeatRequestDto(
        contractVersion: AgentContractVersion.ContractVersion,
        agentVersion: "1.2.3",
        sentAtUtc: new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc),
        lastSuccessfulSyncUtc: new DateTime(2026, 6, 5, 7, 0, 0, DateTimeKind.Utc),
        serviceState: "Running",
        pushQueueDepth: 4,
        pushQueueErrorCount: 1,
        lastRunOutcome: "Success",
        diskFreeBytes: 123456789L);

    private static HttpPlatformClient CreateClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://platform.test/") };
        return new HttpPlatformClient(http, "prefix.secret");
    }
}
