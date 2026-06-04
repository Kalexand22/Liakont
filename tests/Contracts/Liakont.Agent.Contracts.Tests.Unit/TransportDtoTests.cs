namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Xunit;

/// <summary>
/// Tests de construction des DTOs d'enveloppe du contrat (acceptance PIV01 point 7) et de la
/// version du contrat exposée par l'assembly.
/// </summary>
public sealed class TransportDtoTests
{
    [Fact]
    public void ContractVersion_Should_Be_Exposed()
    {
        AgentContractVersion.ContractVersion.Should().Be("1");
        AgentContractVersion.Current.Should().Be("v1");
    }

    [Fact]
    public void PushBatchRequest_Should_Default_Documents_To_Empty()
    {
        var request = new PushBatchRequestDto(AgentContractVersion.ContractVersion);

        request.ContractVersion.Should().Be("1");
        request.Documents.Should().BeEmpty();
    }

    [Fact]
    public void PushBatchRequest_Should_Carry_Documents()
    {
        var doc = new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "F-1",
            issueDate: new DateTime(2026, 3, 1),
            sourceReference: "ref-1",
            supplier: new PivotPartyDto("Fournisseur Fictif"),
            totals: new PivotTotalsDto(10m, 2m, 12m),
            operationCategory: OperationCategory.PrestationServices);

        var request = new PushBatchRequestDto("1", new[] { doc });

        request.Documents.Should().ContainSingle().Which.Number.Should().Be("F-1");
    }

    [Fact]
    public void PushBatchResponse_Should_Carry_Per_Document_Results()
    {
        var response = new PushBatchResponseDto(new[]
        {
            new DocumentPushResultDto("ref-1", DocumentPushStatus.Accepted),
            new DocumentPushResultDto("ref-2", DocumentPushStatus.Duplicate),
            new DocumentPushResultDto("ref-3", DocumentPushStatus.Rejected, reason: "payload non conforme"),
        });

        response.Results.Should().HaveCount(3);
        response.Results[2].Status.Should().Be(DocumentPushStatus.Rejected);
        response.Results[2].Reason.Should().Be("payload non conforme");
    }

    [Fact]
    public void AgentConfiguration_Should_Default_To_A_Safe_No_Update_State()
    {
        // Tant que le registre de versions (OPS07) est vide : pas de mise à jour forcée.
        var config = new AgentConfigurationDto();

        config.UpdateRequired.Should().BeFalse();
        config.UpdateUrl.Should().BeNull();
        config.LatestAgentVersion.Should().BeNull();
        config.VersionManifestSignature.Should().BeNull();
        config.ExtractionSchedule.Should().BeNull();
    }

    [Fact]
    public void HeartbeatResponse_Should_Wrap_Configuration_And_Server_Time()
    {
        var now = new DateTime(2026, 6, 4, 8, 0, 0, DateTimeKind.Utc);
        var config = new AgentConfigurationDto(extractionSchedule: "0 2 * * *", updateRequired: true, updateUrl: "https://example.test/agent.msi");

        var response = new HeartbeatResponseDto(now, config);

        response.ServerTimeUtc.Should().Be(now);
        response.Configuration.UpdateRequired.Should().BeTrue();
        response.Configuration.UpdateUrl.Should().Be("https://example.test/agent.msi");
    }

    [Fact]
    public void HeartbeatRequest_Should_Carry_Agent_And_Contract_Versions()
    {
        var sentAt = new DateTime(2026, 6, 4, 7, 0, 0, DateTimeKind.Utc);

        var heartbeat = new HeartbeatRequestDto(
            contractVersion: "1",
            agentVersion: "1.0.0",
            sentAtUtc: sentAt,
            lastSuccessfulSyncUtc: sentAt.AddHours(-6));

        heartbeat.ContractVersion.Should().Be("1");
        heartbeat.AgentVersion.Should().Be("1.0.0");
        heartbeat.SentAtUtc.Should().Be(sentAt);
        heartbeat.LastSuccessfulSyncUtc.Should().Be(sentAt.AddHours(-6));
    }

    [Fact]
    public void SourceTaxRegime_Should_Carry_Code_Label_And_Occurrences()
    {
        var regime = new SourceTaxRegimeDto(code: "6", label: "Régime de la marge", occurrences: 42);

        regime.Code.Should().Be("6");
        regime.Label.Should().Be("Régime de la marge");
        regime.Occurrences.Should().Be(42);
    }

    [Fact]
    public void VatCategory_Should_Cover_The_Uncl5305_Codes()
    {
        System.Enum.GetNames<VatCategory>().Should()
            .BeEquivalentTo("S", "AA", "AAA", "Z", "E", "AE", "G", "K", "O");
    }

    [Fact]
    public void OperationCategory_Should_Cover_The_Three_Natures()
    {
        System.Enum.GetNames<OperationCategory>().Should()
            .BeEquivalentTo("LivraisonBiens", "PrestationServices", "Mixte");
    }
}
