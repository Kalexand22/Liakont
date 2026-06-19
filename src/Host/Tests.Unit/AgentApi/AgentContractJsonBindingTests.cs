namespace Liakont.Host.Tests.Unit.AgentApi;

using System;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
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
        services.ConfigureHttpJsonOptions(options => AgentApiJson.ConfigureContractBinding(options.SerializerOptions));
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
    public void Unknown_member_in_v1_payload_is_rejected_not_dropped()
    {
        // RDL04 — sens N+1→N (déploiement non atomique). Un agent portant un champ POST-v1 dans un
        // payload déclaré « 1 » : STJ DROPPE le membre inconnu par défaut → la plateforme re-sérialise
        // un DTO amputé → empreinte plateforme ≠ empreinte agent → anti-doublon (PIV04) / altération
        // (TRK03) cassés. Défaut SÛR : REJET au model-binding (→ 400), jamais droppé silencieusement.
        // Membre inconnu au niveau de l'ENVELOPPE du lot.
        const string batchWithUnknownEnvelopeMember =
            "{\"ContractVersion\":\"1\",\"Documents\":[],\"FutureV2Field\":\"x\"}";

        Action bindEnvelope = () => JsonSerializer.Deserialize<PushBatchRequestDto>(
            batchWithUnknownEnvelopeMember, HostMinimalApiOptions());

        bindEnvelope.Should().Throw<JsonException>(
            "un membre inconnu de l'enveloppe est REFUSÉ (pas droppé) — intégrité du contrat (RDL04)");

        // Membre inconnu DANS un document pivot (le cas qui casse réellement le hash : c'est le document
        // qui est re-sérialisé puis hashé). Le document de référence + un champ post-v1 injecté.
        PivotDocumentDto reference = ContractFixtures.GetDocument("facture-standard-b2c");
        string documentWithUnknownMember = InsertUnknownMember(CanonicalJson.Serialize(reference));
        string batchWithUnknownDocumentMember =
            "{\"ContractVersion\":\"1\",\"Documents\":[" + documentWithUnknownMember + "]}";

        Action bindDocument = () => JsonSerializer.Deserialize<PushBatchRequestDto>(
            batchWithUnknownDocumentMember, HostMinimalApiOptions());

        bindDocument.Should().Throw<JsonException>(
            "un membre inconnu d'un document pivot est REFUSÉ — sinon hash amputé, anti-doublon/altération cassés (RDL04)");
    }

    // Injecte un membre inconnu (post-v1) juste après l'accolade ouvrante d'un objet JSON canonique.
    private static string InsertUnknownMember(string canonicalDocumentJson) =>
        "{\"FutureV2Field\":\"x\"," + canonicalDocumentJson[1..];

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

    // RDL02 — fermeture de la boucle de hash RÉELLE wire→STJ→writer→hash.
    // En PRODUCTION, la plateforme ne hashe PAS les octets reçus de l'agent : elle les RE-DÉSÉRIALISE
    // via System.Text.Json (AgentApiEndpoints), puis CanonicalJson.Serialize + PayloadHasher.ComputeHash
    // sur le DTO STJ-désérialisé (IngestDocumentBatchHandler). Les golden de ContractFixtureTests prouvent
    // writer↔writer sur DTO EN MÉMOIRE ; l'axe STJ(fil)→writer→hash, dont dépendent l'anti-doublon (PIV04)
    // et la détection d'altération (TRK03), n'avait AUCUNE ancre de test (il « marchait par chance » du
    // comportement de STJ). Ces tests l'ancrent avec les options STJ RÉELLES du Host.

    [Theory]
    [MemberData(nameof(ContractFixtures.DocumentCases), MemberType = typeof(ContractFixtures))]
    public void Wire_document_deserialized_by_host_stj_rehashes_to_frozen_imprint(string name)
    {
        PivotDocumentDto fixture = ContractFixtures.GetDocument(name);
        string canonicalWire = CanonicalJson.Serialize(fixture);

        // L'empreinte FIGÉE du contrat = le hash des octets canoniques du golden. L'identité
        // golden↔empreinte figée (FrozenHashes) est ancrée cross-runtime par ContractFixtureTests
        // (ComputeHash(golden) == FrozenHashes[name]) ; ici on prouve l'AUTRE moitié : le DTO obtenu
        // par STJ depuis le fil re-hashe vers cette même empreinte.
        string frozenImprint = PayloadHasher.ComputeHash(canonicalWire);

        string batchWire = ComposeSingleDocumentBatch(fixture);
        PushBatchRequestDto? request = JsonSerializer.Deserialize<PushBatchRequestDto>(batchWire, HostMinimalApiOptions());

        request.Should().NotBeNull();
        request!.Documents.Should().ContainSingle();
        PayloadHasher.ComputeHash(request.Documents[0]).Should().Be(
            frozenImprint,
            "l'axe wire→STJ→writer→hash doit reproduire l'empreinte figée (golden « {0} ») — sinon anti-doublon PIV04 / altération TRK03 cassés",
            name);
    }

    [Fact]
    public void Fully_populated_wire_via_host_stj_rehashes_identically_including_due_date_and_reason_code()
    {
        // Couvre l'axe STJ sur les champs ABSENTS des 8 golden : PaymentDueDate (BT-9, EXT01) et
        // PivotDocumentChargeDto.ReasonCode. Un champ STJ-désérialisé puis oublié changerait le hash.
        PivotDocumentDto full = ContractFixtures.BuildFullyPopulatedDocument();
        string expected = PayloadHasher.ComputeHash(CanonicalJson.Serialize(full));

        string batchWire = ComposeSingleDocumentBatch(full);
        PushBatchRequestDto? request = JsonSerializer.Deserialize<PushBatchRequestDto>(batchWire, HostMinimalApiOptions());

        request.Should().NotBeNull();
        PivotDocumentDto roundTripped = request!.Documents[0];
        PayloadHasher.ComputeHash(roundTripped).Should().Be(
            expected,
            "le chemin STJ doit préserver TOUS les champs, y compris ceux non couverts par les golden (BT-9, ReasonCode)");
        roundTripped.PaymentDueDate.Should().Be(new DateTime(2026, 3, 31), "BT-9 doit survivre à la liaison STJ");
        roundTripped.DocumentCharges.Should().ContainSingle();
        roundTripped.DocumentCharges[0].ReasonCode.Should().Be("ECO", "le code de motif de charge doit survivre à la liaison STJ");
    }

    [Fact]
    public void Unknown_member_on_non_contract_type_is_tolerated_not_rejected()
    {
        // RDL04 — garde du périmètre de l'assembly-guard. Le modificateur RejectUnknownContractMembers
        // applique JsonUnmappedMemberHandling.Disallow UNIQUEMENT aux types dont l'assembly est
        // Liakont.Agent.Contracts (typeInfo.Type.Assembly == typeof(AgentContractVersion).Assembly).
        // Les types HORS de cet assembly doivent rester PERMISSIFS (membre inconnu droppé, pas rejeté) :
        // c'est le comportement par défaut des options « Web » pour tous les endpoints console.
        // Si un refactor supprime la garde d'assembly (jugée « redondante »), TOUS les endpoints
        // minimal-API switchent silencieusement vers le rejet strict — régression invisible côté clients.
        // Ce test verrouille que le périmètre de RDL04 ne s'étend PAS aux types du contexte test/console.
        const string jsonWithUnknownMember =
            "{\"Name\":\"test\",\"Value\":42,\"UnknownFutureField\":\"droppé\"}";

        ConsoleTestPocoDto? result = null;
        Action deserialize = () =>
            result = JsonSerializer.Deserialize<ConsoleTestPocoDto>(jsonWithUnknownMember, HostMinimalApiOptions());

        deserialize.Should().NotThrow(
            "un membre inconnu sur un type hors Liakont.Agent.Contracts est droppé, jamais rejeté (périmètre RDL04)");
        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(42);
    }

    // Enveloppe de lot minimale portant UN document canonique (mêmes noms de propriété que
    // PushBatchRequestDto, version de contrat de l'assembly) — la forme fil réellement POSTée par l'agent.
    private static string ComposeSingleDocumentBatch(PivotDocumentDto document) =>
        "{\"ContractVersion\":\"" + AgentContractVersion.ContractVersion + "\",\"Documents\":["
        + CanonicalJson.Serialize(document) + "]}";

    // POCO LOCAL — vit dans l'assembly de test (PAS dans Liakont.Agent.Contracts).
    // Représente n'importe quel DTO console/minimal-API hors du contrat agent.
    // Utilisé uniquement par Unknown_member_on_non_contract_type_is_tolerated_not_rejected.
    private sealed record ConsoleTestPocoDto(string Name, int Value);
}
