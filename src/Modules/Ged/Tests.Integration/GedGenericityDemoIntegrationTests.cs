namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Infrastructure.Mapping;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration base RÉELLE prouvant la GÉNÉRICITÉ PAR CONFIGURATION (GED10, F19 §11 D12) : les seeds
/// FICTIFS de <c>deployments/demo-ged/</c> (enchères + BTP) sont appliqués tels quels, puis deux métiers radicalement
/// différents sont indexés sur le MÊME schéma <c>ged_index</c> SANS un seul ALTER TABLE. Prouvent : (1) indexer des
/// bordereaux et filtrer par <c>numero_lot</c> remonte TOUS les documents du lot, sans faux positif sur un axe
/// multi-valeur (patron de requête F19 §6.2) ; (2) le MÊME <c>document_axis_links</c> porte un montant EUR (échelle 2)
/// ET un avancement % (échelle 0). Aucun vocabulaire métier n'est codé en dur (règle 7) — il vient de deployments/.
/// </summary>
[Collection("GedIntegration")]
public sealed class GedGenericityDemoIntegrationTests
{
    private static readonly string[] Lots1And2 = ["L-001", "L-002"];
    private static readonly string[] Lot1 = ["L-001"];
    private static readonly string[] Lot3 = ["L-003"];

    private readonly GedDatabaseFixture _fixture;

    public GedGenericityDemoIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Encheres_demo_indexes_bordereaux_and_filters_by_lot_without_multivalue_false_positive()
    {
        var factory = _fixture.CreateTenantDatabase();
        await ApplyDemoSeedAsync(factory, "encheres");
        var indexer = BuildIndexer(factory);

        // 3 bordereaux fictifs : deux portent le lot L-001 (dont un AUSSI L-002 — axe multi-valeur), un porte L-003.
        var bordereau1 = await IndexBordereauAsync(indexer, "BORD-1", lots: Lots1And2, vente: "V-100", acheteurRef: "ACH-1", acheteurNom: "Client 1");
        var bordereau2 = await IndexBordereauAsync(indexer, "BORD-2", lots: Lot1, vente: "V-100", acheteurRef: "ACH-2", acheteurNom: "Client 2");
        _ = await IndexBordereauAsync(indexer, "BORD-3", lots: Lot3, vente: "V-101", acheteurRef: "ACH-1", acheteurNom: "Client 1");

        var documentsOfLot1 = await FilterByAxisAsync(factory, "numero_lot", "L-001");

        documentsOfLot1.Should().BeEquivalentTo(new[] { bordereau1, bordereau2 },
            "filtrer par numero_lot remonte TOUS les documents du lot — le bordereau multi-lot (L-001+L-002) n'apparaît qu'UNE fois (pas de faux positif multi-valeur, F19 §6.2)");

        // L'entité « acheteur » (paramétrage tenant) est bien matérialisée par observation.
        (await CountEntityInstancesAsync(factory)).Should().BeGreaterThan(0, "les entités acheteur sont créées génériquement depuis le profil");
    }

    [Fact]
    public async Task Second_metier_BTP_shares_the_same_schema_carrying_EUR_and_percent_without_alter_table()
    {
        var factory = _fixture.CreateTenantDatabase();

        // Les DEUX métiers coexistent dans la MÊME base / le MÊME schéma (preuve : aucun ALTER TABLE entre eux).
        await ApplyDemoSeedAsync(factory, "encheres");
        await ApplyDemoSeedAsync(factory, "btp");
        var indexer = BuildIndexer(factory);

        // Une situation de travaux : montant HT cumulé en EUR (échelle 2) + avancement en % (échelle 0), sur le
        // MÊME ged_index.document_axis_links que les bordereaux enchères — sans un seul ALTER TABLE.
        var situationId = Guid.NewGuid();
        var ingested = new IngestedDocumentDto(
            sourceReference: "SIT-1",
            documentType: "situation_travaux",
            sourceFields: new Dictionary<string, string>
            {
                ["numero_situation"] = "S-7",
                ["mois"] = "2026-06",
                ["montant_ht_cumule"] = "1234.56",
                ["avancement_pct"] = "75",
                ["chantier_ref"] = "CH-1",
                ["chantier_nom"] = "Chantier 1",
            });
        var outcome = await indexer.IndexAsync(new GedIndexRequest(situationId, ingested, "import"), CancellationToken.None);
        outcome.Should().Be(GedIndexOutcome.Indexed);

        var numbers = await ReadNumberAxesAsync(factory, situationId);

        numbers.Should().ContainKey("montant_ht_cumule").WhoseValue.Should().Be(1234.56m, "un montant EUR est un decimal exact half-up (échelle 2, jamais float)");
        numbers.Should().ContainKey("avancement_pct").WhoseValue.Should().Be(75m, "un pourcentage (échelle 0) partage la MÊME colonne value_number");
    }

    private static GedDocumentIndexer BuildIndexer(IConnectionFactory factory) =>
        new(
            new GedMappingProfileRepository(factory),
            new PostgresAxisCatalog(factory),
            new PostgresEntityCatalog(factory),
            new PostgresGedIndexUnitOfWorkFactory(factory),
            NullLogger<GedDocumentIndexer>.Instance);

    private static async Task<Guid> IndexBordereauAsync(
        GedDocumentIndexer indexer, string sourceReference, string[] lots, string vente, string acheteurRef, string acheteurNom)
    {
        var id = Guid.NewGuid();
        var ingested = new IngestedDocumentDto(
            sourceReference: sourceReference,
            documentType: "bordereau_acheteur",
            sourceFields: new Dictionary<string, string>
            {
                ["numero_vente"] = vente,
                ["acheteur_ref"] = acheteurRef,
                ["acheteur_nom"] = acheteurNom,
            },
            sourceAxes: new[] { new RawAxisHint("numero_lot", lots) });

        var outcome = await indexer.IndexAsync(new GedIndexRequest(id, ingested, "import"), CancellationToken.None);
        outcome.Should().Be(GedIndexOutcome.Indexed, "le bordereau « {0} » est mappé par le profil validé du seed", sourceReference);
        return id;
    }

    // Patron de requête multi-axes F19 §6.2 réduit à UN axe : on compte sur normalized_value (casefold), et le
    // GROUP BY garantit qu'un document à axe MULTI-valeur (deux lots) ne remonte qu'UNE fois pour un lot donné.
    private static async Task<IReadOnlyList<Guid>> FilterByAxisAsync(IConnectionFactory factory, string axisCode, string rawValue)
    {
        var normalized = ValueNormalizer.Normalize(AxisDataType.Text, valueScale: null, rawValue).NormalizedValue;
        using var connection = await factory.OpenAsync();
        var ids = await connection.QueryAsync<Guid>(
            """
            SELECT dal.managed_document_id
            FROM ged_index.current_axis_links dal
            JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
            WHERE ad.code = @AxisCode AND dal.normalized_value = @Normalized
            GROUP BY dal.managed_document_id
            """,
            new { AxisCode = axisCode, Normalized = normalized });
        return ids.ToList();
    }

    private static async Task<Dictionary<string, decimal>> ReadNumberAxesAsync(IConnectionFactory factory, Guid managedDocumentId)
    {
        using var connection = await factory.OpenAsync();
        var rows = await connection.QueryAsync<(string Code, decimal Value)>(
            """
            SELECT ad.code AS Code, dal.value_number AS Value
            FROM ged_index.current_axis_links dal
            JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
            WHERE dal.managed_document_id = @Id AND dal.value_number IS NOT NULL
            """,
            new { Id = managedDocumentId });
        return rows.ToDictionary(r => r.Code, r => r.Value, StringComparer.Ordinal);
    }

    private static async Task<long> CountEntityInstancesAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.entity_instances");
    }

    private static async Task ApplyDemoSeedAsync(IConnectionFactory factory, string vertical)
    {
        var path = Path.Combine(FindDeploymentsRoot(), "demo-ged", vertical, "ged-catalog.sql");
        var sql = await File.ReadAllTextAsync(path);
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    private static string FindDeploymentsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "deployments");
            if (Directory.Exists(Path.Combine(candidate, "demo-ged")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Répertoire deployments/demo-ged introuvable depuis {AppContext.BaseDirectory} — la démo doit être reconstructible depuis deployments/.");
    }
}
