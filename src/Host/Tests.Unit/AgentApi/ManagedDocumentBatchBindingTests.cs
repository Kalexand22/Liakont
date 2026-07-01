namespace Liakont.Host.Tests.Unit.AgentApi;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Ged.Serialization;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Host.AgentApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

/// <summary>
/// Garde la liaison JSON du contrat d'ingestion GÉNÉRIQUE GED (POST /api/agent/v1/managed-documents/batch, GED05b).
/// Le corps <see cref="ManagedDocumentBatchRequestDto"/> et surtout l'<c>IngestedDocumentDto</c> IMMUABLE (constructeur
/// paramétré, <c>IReadOnlyDictionary&lt;string,string&gt;</c> et <c>IReadOnlyList&lt;…&gt;</c> en paramètres) doivent se
/// LIER via les options minimal-API RÉELLES du Host. Les tests d'intégration appellent le handler MediatR en mémoire et
/// ne traversent JAMAIS cette liaison HTTP : ce trou (DTO immuable non désérialisable) serait invisible.
/// <para>La couverture CLÉ est le round-trip <c>wire → STJ → GedCanonicalJson → hash</c> : la plateforme ne hashe PAS
/// les octets reçus, elle RE-SÉRIALISE le DTO STJ-désérialisé (GedCanonicalJson) puis le hashe (registre GED). Si STJ
/// abandonnait un champ de l'<c>IngestedDocumentDto</c>, l'empreinte plateforme divergerait de celle de l'agent →
/// anti-doublon GED (INV-GED-06) cassé. Ce test ancre cet axe avec les options RÉELLES du Host.</para>
/// </summary>
public sealed class ManagedDocumentBatchBindingTests
{
    private static JsonSerializerOptions HostMinimalApiOptions()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(options => AgentApiJson.ConfigureContractBinding(options.SerializerOptions));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Ged_batch_wire_binds_to_the_immutable_dto_and_rehashes_identically()
    {
        var document = new IngestedDocumentDto(
            sourceReference: "PV-2026-42",
            documentType: "PV_VENTE",
            sourceTimestampUtc: new DateTime(2026, 7, 1, 10, 30, 0, DateTimeKind.Utc),
            content: new IngestedContentRef("blob/pv-42.pdf", "application/pdf", 12345, "abcd1234"),
            sourceFields: new Dictionary<string, string> { ["date_cloture"] = "2026-06-30", ["Réf facture"] = "F-77" },
            sourceAxes: new[] { new RawAxisHint("lots", new List<string> { "12", "13" }) },
            sourceEntities: new[] { new RawEntityHint("seller", "S-9", "Étude Dupont") },
            sourceRelations: new[] { new RawRelationHint("concerne", "V-5", "vente") });

        // Empreinte plateforme attendue = hash du JSON canonique GED du DTO en mémoire (registre GED, INV-GED-06).
        var expected = PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(document));

        var request = new ManagedDocumentBatchRequestDto(
            new[] { document },
            new ManagedExtractorCapabilitiesDto(providesManagedDocuments: true, providesAxes: true));

        // Aller-retour par les options STJ RÉELLES du Host (le fil PascalCase que l'agent POSTe).
        var wire = JsonSerializer.Serialize(request, HostMinimalApiOptions());
        var bound = JsonSerializer.Deserialize<ManagedDocumentBatchRequestDto>(wire, HostMinimalApiOptions());

        bound.Should().NotBeNull("le DTO de lot GED immuable doit se lier via les options minimal-API du Host");
        bound!.Documents.Should().ContainSingle();
        PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(bound.Documents[0])).Should().Be(
            expected,
            "l'axe wire→STJ→GedCanonicalJson→hash doit préserver TOUS les champs de l'IngestedDocumentDto (sinon anti-doublon GED INV-GED-06 cassé)");

        // Les capacités déclarées se lient aussi (même si l'endpoint ne les consomme pas en V1) : le contrat est fixé.
        bound.Capabilities.Should().NotBeNull();
        bound.Capabilities!.ProvidesManagedDocuments.Should().BeTrue();
        bound.Capabilities.ProvidesAxes.Should().BeTrue();
    }
}
