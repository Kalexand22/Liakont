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
/// Garde la jambe de DÉSÉRIALISATION System.Text.Json du re-hash plateforme (PIV04), sur laquelle
/// reposent l'anti-doublon (<c>payload_hash</c>) et l'archive WORM (RL-SER-1, redline ADR-0003/0007).
/// <para>L'agent calcule l'empreinte figée sur SON JSON canonique (PIV02) et pousse ce JSON canonique
/// sur le fil. Côté plateforme, <c>IngestDocumentBatchHandler</c> NE relit PAS via
/// <c>PivotCanonicalJsonReader</c> (réservé au chemin de staging) : il re-dérive l'empreinte en
/// RE-sérialisant via <c>CanonicalJson</c> le DTO produit par la désérialisation STJ du corps fil
/// (<c>CanonicalJson.Serialize(document)</c> → <c>PayloadHasher.ComputeHash</c>, cf.
/// <c>IngestDocumentBatchHandler.ProcessDocumentAsync</c>). L'intégrité fiscale exige
/// <c>STJ.Deserialize(canonique) → CanonicalJson.Serialize == canonique</c> OCTET POUR OCTET — vrai
/// aujourd'hui mais gardé par AUCUN test : une montée .NET, un changement du parseur decimal STJ ou un
/// nouveau champ string à échappement divergent casserait silencieusement le statut (Pending éternel),
/// créerait de faux nouveaux documents ou un doublon WORM.</para>
/// <para>Le test traverse les <see cref="HttpJsonOptions"/> RÉELLES du pipeline minimal-API (celles
/// d'<see cref="AgentApiJson"/>, défauts « Web » inclus), comme l'endpoint POST
/// <c>/api/agent/v1/documents/batch</c>. Il vit côté Host (et non dans les fixtures cross-runtime
/// <c>tests/_shared/contract-v1</c>) : la jambe STJ n'existe QUE sur la plateforme — l'agent net48
/// hashe par son writer manuel, jamais par re-désérialisation.</para>
/// </summary>
public sealed class AgentContractRehashIntegrityTests
{
    // Options RÉELLES du pipeline minimal-API : ConfigureHttpJsonOptions alimente
    // Microsoft.AspNetCore.Http.Json.JsonOptions (initialisé aux défauts « Web »). On résout l'instance
    // effectivement injectée — pas un JsonSerializerOptions reconstruit à la main — via le MÊME appel et
    // le MÊME helper que la production (AppBootstrap). Identique à AgentContractJsonBindingTests.
    private static JsonSerializerOptions HostMinimalApiOptions()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(options => AgentApiJson.ConfigureContractBinding(options.SerializerOptions));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
    }

    [Theory]
    [MemberData(nameof(ContractFixtures.DocumentCases), MemberType = typeof(ContractFixtures))]
    public void Stj_rehash_of_wire_document_reproduces_the_canonical_and_frozen_hash(string name)
    {
        // L'agent pousse SON JSON canonique sur le fil ; l'empreinte figée est calculée dessus.
        PivotDocumentDto document = ContractFixtures.GetDocument(name);
        string agentCanonical = CanonicalJson.Serialize(document);
        string frozenHash = PayloadHasher.ComputeHash(agentCanonical);

        // Désérialisation par les options RÉELLES de l'endpoint d'ingestion (chemin HTTP réel, PIV04).
        PushBatchRequestDto? request =
            JsonSerializer.Deserialize<PushBatchRequestDto>(ComposeWireBatch(agentCanonical), HostMinimalApiOptions());

        request.Should().NotBeNull();
        request!.Documents.Should().ContainSingle();

        // Reproduit EXACTEMENT la chaîne de re-hash de PIV04 : CanonicalJson.Serialize(dtoSTJ) → ComputeHash.
        string platformCanonical = CanonicalJson.Serialize(request.Documents[0]);
        string platformHash = PayloadHasher.ComputeHash(platformCanonical);

        const string canonicalBecause = "la re-sérialisation canonique du DTO désérialisé par STJ (chemin HTTP réel) doit reproduire le JSON canonique de l'agent OCTET POUR OCTET — sinon le re-hash plateforme diverge";
        const string hashBecause = "le re-hash plateforme (PIV04) doit égaler l'empreinte figée de l'agent : l'anti-doublon (payload_hash) et l'archive WORM en dépendent (RL-SER-1)";

        platformCanonical.Should().Be(agentCanonical, canonicalBecause);
        platformHash.Should().Be(frozenHash, hashBecause);
    }

    [Fact]
    public void A_single_byte_source_deviation_propagates_through_the_stj_rehash_path()
    {
        // PREUVE PAR MUTATION : la chaîne STJ → CanonicalJson → hash n'est PAS un no-op qui avalerait une
        // déviation. On change exactement UN octet d'une valeur du document fil (un chiffre d'un montant) et
        // on prouve que la re-sérialisation canonique ET le re-hash divergent tous deux — donc une vraie
        // déviation d'un octet de la jambe désérialisation/re-sérialisation serait bien capturée par la
        // garde positive ci-dessus.
        PivotDocumentDto document = ContractFixtures.GetDocument("facture-standard-b2c");
        string agentCanonical = CanonicalJson.Serialize(document);
        string frozenHash = PayloadHasher.ComputeHash(agentCanonical);

        // « "NetAmount":120.00 » → « "NetAmount":120.01 » : un seul chiffre du NetAmount de la ligne change.
        const string token = "\"NetAmount\":120.00";
        int index = agentCanonical.IndexOf(token, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, "le jeton à muter doit exister dans le JSON canonique golden");
        string mutated = agentCanonical.Remove(index, token.Length).Insert(index, "\"NetAmount\":120.01");
        mutated.Should().NotBe(agentCanonical, "la mutation doit réellement changer un octet de la source");

        PushBatchRequestDto? request =
            JsonSerializer.Deserialize<PushBatchRequestDto>(ComposeWireBatch(mutated), HostMinimalApiOptions());
        string platformCanonical = CanonicalJson.Serialize(request!.Documents[0]);

        platformCanonical.Should().NotBe(
            agentCanonical, "une déviation d'un octet en source se propage à la re-sérialisation canonique");
        PayloadHasher.ComputeHash(platformCanonical).Should().NotBe(
            frozenHash, "donc le re-hash plateforme diffère de l'empreinte figée — la garde a des dents");
    }

    // Compose le corps fil d'un lot EXACTEMENT comme l'agent le pousse : chaque document embarqué EST son
    // JSON canonique (PIV02), enveloppé dans le DTO PushBatchRequestDto (noms de propriété exacts, version
    // de contrat de l'assembly). Miroir de ContractFixtures.ComposeBatchRequestJson, pour un document.
    private static string ComposeWireBatch(string canonicalDocument) =>
        "{\"ContractVersion\":\"" + AgentContractVersion.ContractVersion + "\",\"Documents\":["
        + canonicalDocument + "]}";
}
