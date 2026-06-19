namespace Liakont.Host.Tests.Unit.AgentApi;

using System;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Host.AgentApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

/// <summary>
/// Garde la liaison JSON du contrat agent (POST /api/agent/v1/documents/batch). Le modèle pivot émet
/// ses énumérations par leur NOM (cf. <c>CanonicalJson</c> et les fixtures contrat-v1) ; System.Text.Json
/// attend par défaut un nombre pour un enum, et rejetait donc un lot au format documenté en 400 au
/// model-binding, AVANT le handler. Ce trou était invisible : les tests d'ingestion appellent le handler
/// MediatR en mémoire et ne traversent jamais la liaison JSON HTTP.
/// <para>Le test résout les <see cref="HttpJsonOptions"/> RÉELLES du pipeline minimal-API (celles que
/// <c>ConfigureHttpJsonOptions</c> alimente, défauts « Web » inclus) après application du helper de
/// production <see cref="AgentApiJson"/>, puis désérialise le lot de référence
/// (<see cref="ContractFixtures.ComposeBatchRequestJson"/>, format fil documenté, enums en chaînes). Un
/// contrôle négatif reproduit le mode d'échec sans les convertisseurs.</para>
/// <para>Portée : ce test garde le helper de liaison ET son intégration dans les options minimal-API
/// réelles. Il ne POST PAS sur l'endpoint (la liaison HTTP de bout en bout — auth par clé agent +
/// résolution tenant + démarrage avec migration de base) : ce test d'intégration via
/// <c>WebApplicationFactory</c> est porté par le lot AGT, qui construit le transport agent réel et son
/// harnais HTTP. Tant que ce harnais n'existe pas, l'unique ligne de câblage
/// <c>AppBootstrap.ConfigureHttpJsonOptions(... AgentApiJson ...)</c> n'est pas gardée par un test.</para>
/// </summary>
public sealed class AgentContractJsonBindingTests
{
    // Options RÉELLES du pipeline minimal-API : ConfigureHttpJsonOptions alimente
    // Microsoft.AspNetCore.Http.Json.JsonOptions (initialisé aux défauts « Web »). On résout l'instance
    // effectivement injectée — pas un JsonSerializerOptions reconstruit à la main — via le MÊME appel et
    // le MÊME helper que la production (AppBootstrap).
    private static JsonSerializerOptions HostMinimalApiOptions()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(options => AgentApiJson.ConfigureContractEnums(options.SerializerOptions));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Documented_batch_with_string_enums_binds_to_dto()
    {
        string json = ContractFixtures.ComposeBatchRequestJson();

        PushBatchRequestDto? request = JsonSerializer.Deserialize<PushBatchRequestDto>(json, HostMinimalApiOptions());

        request.Should().NotBeNull();
        request!.Documents.Should().HaveCount(2);

        // facture-standard-b2c : OperationCategory "LivraisonBiens", ligne 1 CategoryCode "S".
        PivotDocumentDto first = request.Documents[0];
        first.OperationCategory.Should().Be(OperationCategory.LivraisonBiens);
        first.Lines.Should().ContainSingle();
        first.Lines[0].Taxes.Should().ContainSingle();
        first.Lines[0].Taxes[0].CategoryCode.Should().Be(VatCategory.S);
    }

    [Fact]
    public void Documented_batch_with_string_enums_is_rejected_without_converters()
    {
        // Contrôle négatif : SANS les convertisseurs, c'est exactement le mode d'échec du finding —
        // les enums en chaîne ne lient pas et le model-binding lève (→ 400 à la frontière HTTP).
        string json = ContractFixtures.ComposeBatchRequestJson();
        var webDefaultsOnly = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Action bind = () => JsonSerializer.Deserialize<PushBatchRequestDto>(json, webDefaultsOnly);

        bind.Should().Throw<JsonException>();
    }

    [Fact]
    public void Out_of_range_integer_enum_is_rejected_by_strict_binding()
    {
        // RDL01 : allowIntegerValues:false sur les convertisseurs de requête. Un entier hors plage
        // ({"OperationCategory":99}) ne doit PAS lier comme une valeur d'enum non définie (qui finirait
        // hashée/archivée en nombre muet) — il est REJETÉ au model-binding (→ 400 à la frontière HTTP),
        // symétrique de la garde WriteEnum côté sérialisation canonique.
        const string json =
            "{\"ContractVersion\":\"1\",\"Documents\":[{\"SourceDocumentKind\":\"FA\",\"Number\":\"FA-1\","
            + "\"IssueDate\":\"2026-01-01\",\"SourceReference\":\"SRC-1\",\"Totals\":{\"TotalNet\":0,"
            + "\"TotalTax\":0,\"TotalGross\":0},\"OperationCategory\":99,\"CurrencyCode\":\"EUR\","
            + "\"Lines\":[],\"CreditNoteRefs\":[],\"Payments\":[],\"DocumentCharges\":[],\"IsSelfBilled\":false}]}";

        Action bind = () => JsonSerializer.Deserialize<PushBatchRequestDto>(json, HostMinimalApiOptions());

        bind.Should().Throw<JsonException>("un entier hors plage est refusé en binding strict (RDL01)");
    }

    [Fact]
    public void Batch_response_status_is_serialized_by_name()
    {
        // Symétrie du contrat : le statut d'ingestion de la RÉPONSE (DocumentPushStatus) doit partir par
        // nom ("Accepted"/"Duplicate"/"Rejected"), pas en nombre (contrat-agent-v1.md §3). Les mêmes
        // Http.Json.JsonOptions servent requête et réponse.
        var response = new PushBatchResponseDto(new[]
        {
            new DocumentPushResultDto("no_ba=4007", DocumentPushStatus.Accepted),
            new DocumentPushResultDto("no_ba=4042", DocumentPushStatus.Rejected, "motif"),
        });

        string json = JsonSerializer.Serialize(response, HostMinimalApiOptions());

        json.Should().Contain("\"Accepted\"").And.Contain("\"Rejected\"");
        json.Should().NotContain("\"status\":1").And.NotContain("\"status\":3");
    }
}
