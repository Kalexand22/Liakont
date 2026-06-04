namespace Liakont.Modules.TvaMapping.Tests.Integration;

using FluentAssertions;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Mapping;
using Liakont.Modules.TvaMapping.Domain.Services;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure.Seed;
using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Import de la table de mapping d'EXEMPLE (item TVA04) sur PostgreSQL réel (Testcontainers) :
/// le seed <c>config/exemples/mapping-exemple.json</c> est importé dans un tenant de test, puis le
/// moteur (TVA02) mappe les documents golden PIV03 (<see cref="ContractFixtures.Documents"/>) et l'on
/// vérifie chaque catégorie produite, le blocage d'un régime absent (defaultBehavior=block), le taux
/// calculé des frais, la levée d'ambiguïté par flags (F03 §3), l'exposition du marqueur « table
/// d'exemple » (« NON VALIDÉE ») et l'isolation par tenant.
/// </summary>
[Collection("TvaMappingIntegration")]
public sealed class MappingSeedImportIntegrationTests
{
    private const string ExampleMarker = "Table d'exemple — usage démo/tests uniquement";
    private static readonly DateTimeOffset MappedAt = new(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly TvaMappingDatabaseFixture _fixture;

    public MappingSeedImportIntegrationTests(TvaMappingDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ExampleSeedPath =>
        Path.Combine(AppContext.BaseDirectory, "config", "exemples", "mapping-exemple.json");

    [Fact]
    public async Task ExampleSeed_Imports_And_Exposes_NonValidated_ExampleMarker()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();

        await ImportAndPersistAsync(harness, companyId);

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto.Should().NotBeNull();
        dto!.MappingVersion.Should().Be("exemple-v1");
        dto.DefaultBehavior.Should().Be("Block");
        dto.ValidatedBy.Should().Be(ExampleMarker, "le marqueur « table d'exemple » est porté par le modèle (item TVA04 §2).");
        dto.ValidatedDate.Should().BeNull();
        dto.IsValidated.Should().BeFalse(
            "une table d'exemple reste « NON VALIDÉE » — le garde-fou PIP01 refuse alors tout envoi en production.");
    }

    [Fact]
    public async Task ExampleSeed_Covers_Every_Uncl5305_Category()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyId);

        var dto = await harness.Queries.GetMappingTable(companyId);
        var covered = dto!.Rules.Select(r => r.Category).ToHashSet(StringComparer.Ordinal);

        foreach (var category in Enum.GetNames<VatCategory>())
        {
            covered.Should().Contain(
                category, $"l'exemple doit couvrir la catégorie UNCL5305 « {category} » (item TVA04).");
        }
    }

    [Fact]
    public async Task ExampleSeed_Maps_Piv03_Golden_Regimes_To_Expected_Categories()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyId);
        var table = await ReloadAsync(harness, companyId);

        var assertedLines = 0;
        foreach (var fixture in ContractFixtures.Documents)
        {
            foreach (var line in fixture.Document.Lines)
            {
                foreach (var tax in line.Taxes)
                {
                    // Une ligne golden « brute » (forme push agent) ne porte pas de catégorie : rien à
                    // vérifier côté mapping (la catégorie est justement ce que la plateforme produit).
                    if (tax.CategoryCode is null)
                    {
                        continue;
                    }

                    line.SourceRegimeCodes.Should().NotBeEmpty(
                        $"le golden « {fixture.Name} » doit porter un régime source à mapper.");

                    // Le moteur clé sur (code régime source, part). Les lignes golden PIV03 sont des
                    // adjudications de vente ; la résolution fine part/code depuis la ligne brute revient
                    // au pipeline (PIP01). Ici on exerce le mapping de catégorie à la part adjudication.
                    var request = new MappingRequest
                    {
                        SourceRegimeCode = line.SourceRegimeCodes[0],
                        Part = MappingPart.Adjudication,
                    };

                    var result = TvaMapper.Map(table, request, MappedAt);

                    result.IsMapped.Should().BeTrue(
                        $"le régime « {line.SourceRegimeCodes[0]} » du golden « {fixture.Name} » doit être couvert par la table d'exemple.");
                    result.Category.Should().Be(tax.CategoryCode);
                    result.Vatex.Should().Be(tax.VatexCode);
                    result.Rate.Should().Be(tax.Rate);
                    assertedLines++;
                }
            }
        }

        assertedLines.Should().BeGreaterThan(0, "au moins une ligne golden porte une catégorie mappée à vérifier.");
    }

    [Fact]
    public async Task ExampleSeed_Blocks_An_Unmapped_Regime()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyId);
        var table = await ReloadAsync(harness, companyId);

        var result = TvaMapper.Map(
            table,
            new MappingRequest { SourceRegimeCode = "REGIME-NON-MAPPE", Part = MappingPart.Adjudication },
            MappedAt);

        result.IsMapped.Should().BeFalse("un régime absent de la table bloque le document (defaultBehavior=block, F03 §4.1).");
        result.Category.Should().BeNull();
        result.Trace.Should().BeNull();
        result.BlockReason.Should().NotBeNullOrWhiteSpace();
        result.BlockReason!.Should().Contain("REGIME-NON-MAPPE");
    }

    [Fact]
    public async Task ExampleSeed_Frais_Rule_Uses_Computed_Rate()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyId);
        var table = await ReloadAsync(harness, companyId);

        var result = TvaMapper.Map(
            table,
            new MappingRequest { SourceRegimeCode = "NORMAL", Part = MappingPart.Frais },
            MappedAt);

        result.IsMapped.Should().BeTrue();
        result.Category.Should().Be(VatCategory.S);
        result.RateMode.Should().Be(RateMode.ComputedFromSource);
        result.Rate.Should().BeNull(
            "le taux des frais est calculé en aval à partir des montants de la ligne (F03 §4.1).");
    }

    [Fact]
    public async Task ExampleSeed_Flag_Based_Rule_Requires_The_Source_Flag()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyId);
        var table = await ReloadAsync(harness, companyId);

        // Sans le flag requis, la règle (VENDEUR-NON-ASSUJETTI, adjudication) ne s'applique pas → blocage.
        var blocked = TvaMapper.Map(
            table,
            new MappingRequest { SourceRegimeCode = "VENDEUR-NON-ASSUJETTI", Part = MappingPart.Adjudication },
            MappedAt);
        blocked.IsMapped.Should().BeFalse(
            "le flag source requis n'est pas satisfait → la règle ne s'applique pas, le document est bloqué (F03 §3).");

        // Avec le flag source attendu, la règle s'applique → E + VATEX-EU-F (déclencheur du régime de la marge).
        var mapped = TvaMapper.Map(
            table,
            new MappingRequest
            {
                SourceRegimeCode = "VENDEUR-NON-ASSUJETTI",
                Part = MappingPart.Adjudication,
                SourceFlags = new Dictionary<string, string> { ["assujetti_tva"] = "false" },
            },
            MappedAt);
        mapped.IsMapped.Should().BeTrue();
        mapped.Category.Should().Be(VatCategory.E);
        mapped.Vatex.Should().Be("VATEX-EU-F");
    }

    [Fact]
    public async Task ExampleSeed_Import_Is_Tenant_Scoped()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyWith = Guid.NewGuid();
        var companyWithout = Guid.NewGuid();
        await ImportAndPersistAsync(harness, companyWith);

        (await harness.Queries.GetMappingTable(companyWith)).Should().NotBeNull();
        (await harness.Queries.GetMappingTable(companyWithout)).Should().BeNull(
            "l'import dans un tenant ne crée aucune table pour un autre tenant (CLAUDE.md n°9).");
    }

    private static async Task ImportAndPersistAsync(TvaMappingHarness harness, Guid companyId)
    {
        var table = await MappingTableSeedReader.ImportFileAsync(ExampleSeedPath, companyId);
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
    }

    private static async Task<MappingTable> ReloadAsync(TvaMappingHarness harness, Guid companyId)
    {
        using var connection = await harness.ConnectionFactory.OpenAsync();
        var table = await TvaMappingMaterializer.LoadByCompanyAsync(
            connection, companyId, transaction: null, CancellationToken.None);
        table.Should().NotBeNull("la table importée doit être rechargeable depuis la base.");
        return table!;
    }
}
