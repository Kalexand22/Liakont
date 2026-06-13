namespace Liakont.Modules.TvaMapping.Tests.Integration;

using System.Text.RegularExpressions;
using FluentAssertions;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Mapping;
using Liakont.Modules.TvaMapping.Domain.Services;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;
using Liakont.Modules.TvaMapping.Infrastructure.Seed;
using Liakont.Modules.TvaMapping.Tests.Integration.Doubles;
using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;
using Stratum.Common.Testing;
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

    // Régimes source poussés par le seed de documents de démo (tools/dev-seed-demo-docs.ps1,
    // champ sourceTaxRegimes du batch) → catégorie attendue de la table de seed par défaut. La table
    // par défaut DOIT couvrir exactement ces régimes en part Autre, sinon le CHECK des documents de
    // démo bloque (« aucune règle applicable ») — item FIX304. Catégories tracées F03 §2.1
    // (20→S, 10/5,5→AA) et règle TAUX-ZERO de mapping-exemple.json (0→Z).
    private static readonly (string Regime, VatCategory Category)[] DemoRegimeExpectations =
    {
        ("20", VatCategory.S),
        ("10", VatCategory.AA),
        ("5.5", VatCategory.AA),
        ("0", VatCategory.Z),
    };

    private readonly TvaMappingDatabaseFixture _fixture;

    public MappingSeedImportIntegrationTests(TvaMappingDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ExampleSeedPath =>
        Path.Combine(AppContext.BaseDirectory, "config", "exemples", "mapping-exemple.json");

    private static string TenantSeedMappingPath =>
        Path.Combine(AppContext.BaseDirectory, "config", "exemples", "tenant-seed", "mapping-tva.json");

    private static string EncheresVariantMappingPath =>
        Path.Combine(AppContext.BaseDirectory, "config", "exemples", "tenant-seed", "encheres", "mapping-tva.json");

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

    [Fact]
    public async Task ImportMappingTableSeedCommand_Imports_NonValidated_And_Is_Idempotent()
    {
        // Item FIX01b : la commande d'import (câblée au point d'entrée OPS03) amorce un tenant vierge
        // depuis le fichier de seed, conserve le marqueur « table d'exemple » (NON VALIDÉE), et reste
        // idempotente (un second import sur un tenant déjà paramétré est ignoré — pas d'écrasement).
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var accessor = new TestActorContextAccessor(Guid.NewGuid(), companyId);
        var filter = new TestCompanyFilter(accessor);
        var handler = new ImportMappingTableSeedHandler(harness.UowFactory, filter, accessor);

        var first = await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = ExampleSeedPath }, CancellationToken.None);
        first.Should().BeTrue("un tenant vierge importe la table de seed.");

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto.Should().NotBeNull();
        dto!.MappingVersion.Should().Be("exemple-v1");
        dto.IsValidated.Should().BeFalse("la table d'exemple reste NON VALIDÉE (garde-fou PIP01).");
        dto.ValidatedBy.Should().Be(ExampleMarker, "le marqueur « table d'exemple » est conservé.");

        var second = await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = ExampleSeedPath }, CancellationToken.None);
        second.Should().BeFalse("une table existante n'est jamais écrasée par un ré-import (idempotent).");
    }

    [Fact]
    public async Task Shipped_TenantSeed_Mapping_File_Imports_And_Is_NonValidated()
    {
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var accessor = new TestActorContextAccessor(Guid.NewGuid(), companyId);
        var filter = new TestCompanyFilter(accessor);
        var handler = new ImportMappingTableSeedHandler(harness.UowFactory, filter, accessor);

        var imported = await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = TenantSeedMappingPath }, CancellationToken.None);
        imported.Should().BeTrue();

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto.Should().NotBeNull();
        dto!.MappingVersion.Should().Be("tenant-seed-exemple-v2");
        dto.IsValidated.Should().BeFalse("le fichier de seed livré est NON VALIDÉE (garde-fou PIP01).");
        dto.Rules.Should().HaveCount(4, "le seed par défaut générique couvre les régimes de démo 20/10/5.5/0 (item FIX304).");
    }

    [Fact]
    public async Task DefaultTenantSeed_Has_No_DeadRules_On_FreshTenant()
    {
        // Acceptation FIX304 : boot vierge + seed par défaut → 0 règle morte au contrôle de cohérence
        // (FIX03). Toutes les règles du seed sont en part Autre — la seule part consultée par le pipeline
        // générique (ConsultedMappingParts.PipelineConsulted) — donc aucune n'est PartNotConsulted. Sur un
        // tenant vierge, aucun régime n'est observé, donc RegimeNeverObserved ne s'applique pas non plus.
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var table = await ImportDefaultTenantSeedAsync(harness, companyId);

        var views = table.Rules
            .Select(r => new MappingRuleConsistencyView
            {
                SourceRegimeCode = r.SourceRegimeCode,
                Part = r.Part,
                Label = r.Label,
            })
            .ToList();

        var report = MappingConsistencyAnalyzer.Analyze(
            views,
            ConsultedMappingParts.PipelineConsulted(),
            Array.Empty<string>(),
            tableConfigured: true);

        report.HasDeadRules.Should().BeFalse(
            "le seed par défaut ne doit signaler AUCUNE règle morte sur un environnement neuf (item FIX304) — "
            + "régimes mortes signalés : "
            + string.Join(", ", report.DeadRules.Select(d => $"{d.SourceRegimeCode}/{d.Part}")));
    }

    [Fact]
    public async Task Encheres_Variant_Seed_Imports_As_Valid_Auction_Table()
    {
        // La variante enchères (config/exemples/tenant-seed/encheres/) reste un fichier de seed VALIDE et
        // SÉPARÉ (item FIX304/F4) : table d'enchères (adjudication/frais, régime de la marge), distincte du
        // seed par défaut générique. Elle s'importe sans erreur fiscale (E + VATEX présent), part Frais incluse.
        var companyId = Guid.NewGuid();

        var table = await MappingTableSeedReader.ImportFileAsync(EncheresVariantMappingPath, companyId);

        table.MappingVersion.Should().Be("tenant-seed-encheres-exemple-v1");
        table.IsValidated.Should().BeFalse("la variante reste NON VALIDÉE (garde-fou PIP01).");
        table.Rules.Should().HaveCount(3);
        table.Rules.Should().Contain(r => r.Part == MappingPart.Adjudication,
            "la variante enchères porte le découpage adjudication.");
        table.Rules.Should().Contain(r => r.Part == MappingPart.Frais,
            "la variante enchères porte le découpage frais acheteur.");
    }

    [Fact]
    public async Task DefaultTenantSeed_Covers_Demo_Document_Regimes()
    {
        // Acceptation FIX304 : le CHECK des documents de démo (régimes 20/10/5.5/0, part Autre) trouve
        // toujours une règle applicable — jamais « aucune règle applicable ». Le moteur clé sur
        // (code régime, part) ; on exerce exactement la requête que CheckTvaMapping construit (part Autre).
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        await ImportDefaultTenantSeedAsync(harness, companyId);
        var table = await ReloadAsync(harness, companyId);

        // Source de vérité : les régimes réellement poussés par le script de démo. Le set attendu doit
        // correspondre EXACTEMENT — si le script gagne/perd un régime, ce test échoue et force la mise à
        // jour du seed par défaut ET de DemoRegimeExpectations (sinon le CHECK réel de la démo bloquerait).
        DemoRegimeCodesFromSeedScript().Should().BeEquivalentTo(
            DemoRegimeExpectations.Select(e => e.Regime),
            "le seed par défaut et le test doivent couvrir EXACTEMENT les régimes de tools/dev-seed-demo-docs.ps1 (cohérence FIX304).");

        foreach (var (regime, expectedCategory) in DemoRegimeExpectations)
        {
            var result = TvaMapper.Map(
                table,
                new MappingRequest { SourceRegimeCode = regime, Part = MappingPart.Autre },
                MappedAt);

            result.IsMapped.Should().BeTrue(
                $"le régime de démo « {regime} » doit trouver une règle (part Autre) — sinon le CHECK bloque « aucune règle applicable ».");
            result.Category.Should().Be(
                expectedCategory, $"le régime de démo « {regime} » est tracé vers la catégorie « {expectedCategory} » (F03 §2.1).");
        }
    }

    [Fact]
    public async Task ImportMappingTableSeedCommand_With_Explicit_CompanyId_Imports_Without_Ambient_Context()
    {
        // Scénario d'amorçage (FIX203a) : au démarrage, AUCUN contexte de société ambiant n'est posé
        // (acteur boot sans tenant → CompanyId null) et le filtre de société échouerait s'il était
        // consulté. Un companyId EXPLICITE doit suffire à importer la table — sinon le dispatch imbriqué
        // de l'import de seed laissait la table TVA jamais amorcée (le seed partiel corrigé).
        var harness = new TvaMappingHarness(_fixture);
        var companyId = Guid.NewGuid();
        var bootAccessor = new StubActorContextAccessor(); // acteur boot : CompanyId == null
        var handler = new ImportMappingTableSeedHandler(harness.UowFactory, new ThrowingCompanyFilter(), bootAccessor);

        var first = await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = ExampleSeedPath, CompanyId = companyId },
            CancellationToken.None);
        first.Should().BeTrue("un companyId explicite amorce la table sans dépendre du contexte ambiant.");

        var dto = await harness.Queries.GetMappingTable(companyId);
        dto.Should().NotBeNull("la table est scopée sur le companyId explicite fourni.");
        dto!.IsValidated.Should().BeFalse("la table d'exemple reste NON VALIDÉE (garde-fou PIP01).");

        // Ré-import (récupération / re-boot) : l'existant n'est JAMAIS écrasé — idempotent par composant.
        var second = await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = ExampleSeedPath, CompanyId = companyId },
            CancellationToken.None);
        second.Should().BeFalse("une table déjà présente pour ce tenant n'est jamais réécrite (create-only).");
    }

    [Fact]
    public async Task ImportMappingTableSeedCommand_With_CompanyId_Conflicting_With_Actor_Is_Rejected()
    {
        // Garde anti-injection cross-tenant (CLAUDE.md n°9/17) : un companyId explicite qui CONTREDIT la
        // société d'un acteur de tenant présent (chemin opérateur) est refusé — empêche une table écrite
        // dans le mauvais tenant. Aligné sur la garde de ImportTenantSeedHandler (FIX01a).
        var harness = new TvaMappingHarness(_fixture);
        var actorCompanyId = Guid.NewGuid();
        var accessor = new TestActorContextAccessor(Guid.NewGuid(), actorCompanyId);
        var handler = new ImportMappingTableSeedHandler(harness.UowFactory, new TestCompanyFilter(accessor), accessor);

        var act = async () => await handler.Handle(
            new ImportMappingTableSeedCommand { SeedFilePath = ExampleSeedPath, CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await harness.Queries.GetMappingTable(actorCompanyId)).Should().BeNull(
            "aucune table n'est écrite quand l'override de société est refusé.");
    }

    /// <summary>
    /// Extrait les codes régime du bloc <c>sourceTaxRegimes</c> du script de démo (source de vérité des
    /// régimes poussés en démo) : le seed par défaut DOIT les couvrir tous (cohérence FIX304). Dériver du
    /// script — au lieu de ré-encoder en dur — fait échouer le test si le script gagne/perd un régime.
    /// </summary>
    private static List<string> DemoRegimeCodesFromSeedScript()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "tools", "dev-seed-demo-docs.ps1");
        File.Exists(scriptPath).Should().BeTrue($"le script de démo doit être copié en sortie : « {scriptPath} ».");
        var script = File.ReadAllText(scriptPath);

        var block = Regex.Match(script, @"sourceTaxRegimes\s*=\s*@\((?<body>.*?)\)", RegexOptions.Singleline);
        block.Success.Should().BeTrue("le script de démo doit déclarer un bloc sourceTaxRegimes.");

        var codes = Regex.Matches(block.Groups["body"].Value, "code\\s*=\\s*\"(?<code>[^\"]+)\"")
            .Select(m => m.Groups["code"].Value)
            .ToList();
        codes.Should().NotBeEmpty("le bloc sourceTaxRegimes doit déclarer au moins un régime.");
        return codes;
    }

    private static async Task ImportAndPersistAsync(TvaMappingHarness harness, Guid companyId)
    {
        var table = await MappingTableSeedReader.ImportFileAsync(ExampleSeedPath, companyId);
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
    }

    /// <summary>Importe et persiste le seed de tenant PAR DÉFAUT (générique, régimes 20/10/5.5/0) et retourne la table construite.</summary>
    private static async Task<MappingTable> ImportDefaultTenantSeedAsync(TvaMappingHarness harness, Guid companyId)
    {
        var table = await MappingTableSeedReader.ImportFileAsync(TenantSeedMappingPath, companyId);
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
        return table;
    }

    private static async Task<MappingTable> ReloadAsync(TvaMappingHarness harness, Guid companyId)
    {
        using var connection = await harness.ConnectionFactory.OpenAsync();
        var table = await TvaMappingMaterializer.LoadByCompanyAsync(
            connection, companyId, transaction: null, CancellationToken.None);
        table.Should().NotBeNull("la table importée doit être rechargeable depuis la base.");
        return table!;
    }

    /// <summary>Filtre de société qui échoue : prouve qu'un companyId explicite ne consulte jamais l'ambiant.</summary>
    private sealed class ThrowingCompanyFilter : ICompanyFilter
    {
        public Guid GetRequiredCompanyId() =>
            throw new InvalidOperationException("Le filtre de société ne doit pas être consulté quand un companyId explicite est fourni.");
    }
}
