namespace Liakont.Modules.Pipeline.Tests.Integration.EndToEnd;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Xunit;

/// <summary>
/// B2C02 — le chemin (déclaration e-reporting B2C 10.3 incluse) est piloté par le PARAMÉTRAGE FISCAL DU
/// TENANT et BLOQUE SÛREMENT quand ce paramétrage ne tranche pas, démontré sur DEUX bases tenant réelles
/// (database-per-tenant). Les déclarations 10.3 empruntent la même voie document que les factures : elles
/// traversent le CHECK (<see cref="Liakont.Modules.Pipeline.Infrastructure.Check.DocumentReceivedConsumer"/>
/// → <see cref="Liakont.Modules.Pipeline.Infrastructure.Check.DocumentCheckEvaluator"/>), qui applique la
/// table de mapping TVA validée du tenant et la nature d'opération du paramétrage fiscal — aucun comportement
/// spécifique 10.3 à ajouter, le pilotage par le tenant est structurel.
/// </summary>
/// <remarks>
/// <para>Couvre les invariants non négociables de B2C02 :</para>
/// <list type="bullet">
///   <item><c>defaultBehavior:block</c> honoré : un régime source ABSENT de la table validée du tenant
///   (ici « 6 », jamais seedé — défaut = bloquer, JAMAIS de mapping deviné, CLAUDE.md n°2/n°3) bloque le
///   document, motif consigné dans la piste d'audit append-only (INV-PIPELINE-011).</item>
///   <item>nature d'opération non paramétrée (<c>OperationCategory null</c>) → document SUSPENDU (Blocked) :
///   la plateforme remplit la nature à l'ingestion depuis le paramétrage fiscal (ADR-0023 amendé) ; absente
///   du paramétrage tenant → laissée nulle → bloquée (jamais devinée).</item>
///   <item>isolation tenant (CLAUDE.md n°9) : chaque tenant est régi par SA propre base/paramétrage ; le
///   blocage est démontré indépendamment sur ≥ 2 bases.</item>
/// </list>
/// <para>Le seed du harnais (<see cref="PipelineE2ETenant"/>) est strictement FICTIF (table validée régime
/// « NORMAL » → S 20 %, CLAUDE.md n°7) ; le régime « 6 » n'est volontairement PAS mappé (et n'est mappé vers
/// aucun VATEX-EU-J dans aucun seed produit — le défaut bloque). La variante « régime de la marge » de la
/// présentation 2 lignes (adjudication E/VATEX + frais S) reste portée par le seed d'exemple
/// <c>config/exemples/tenant-seed/encheres/mapping-tva.json</c>, sans calcul de marge ici (gelé jusqu'à
/// GATE_B2C_SOURCING).</para>
/// </remarks>
public sealed class B2cReportingTenantConfigIntegrationTests
    : IClassFixture<B2cReportingTenantConfigIntegrationTests.MultiTenantFixture>
{
    private readonly MultiTenantFixture _fixture;

    public B2cReportingTenantConfigIntegrationTests(MultiTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DefaultBehaviorBlock_UnmappedRegime_Blocks_Document_On_Both_Tenant_Bases()
    {
        foreach (var (tenant, label) in _fixture.Tenants)
        {
            // Régime source « 6 » ABSENT de la table validée du tenant + nature d'opération renseignée :
            // l'unique cause de blocage est le mapping (defaultBehavior:block honoré, jamais deviné).
            var sourceReference = $"no_ba=b2c02-regime6-{label}";
            var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "6");

            var documentId = await IngestAndCheckAsync(tenant, pivot);

            (await tenant.GetDocumentStateAsync(documentId)).Should().Be(
                "Blocked", $"régime « 6 » non mappé sur la table validée du tenant « {label} » (defaultBehavior:block).");

            var events = await tenant.GetEventsAsync(documentId);
            events.Should().Contain(
                e => e.Detail != null && e.Detail.Contains("table de mapping", StringComparison.Ordinal),
                $"le motif de blocage de mapping est consigné dans la piste d'audit append-only du tenant « {label} » (INV-PIPELINE-011).");
        }
    }

    [Fact]
    public async Task OperationCategoryNull_Suspends_Document_On_Both_Tenant_Bases()
    {
        foreach (var (tenant, label) in _fixture.Tenants)
        {
            // Régime « NORMAL » MAPPÉ (le mapping passe) mais nature d'opération NON portée par le document et
            // ABSENTE du paramétrage fiscal du tenant (le harnais ne seede aucun FiscalSettings) : l'enrichisseur
            // la laisse nulle → l'unique cause de blocage est la nature d'opération manquante (jamais devinée).
            var sourceReference = $"no_ba=b2c02-opcat-null-{label}";
            var pivot = CheckIntegrationFixtures.BuildPivot(
                sourceReference, regimeCode: "NORMAL", operationCategory: null);

            var documentId = await IngestAndCheckAsync(tenant, pivot);

            (await tenant.GetDocumentStateAsync(documentId)).Should().Be(
                "Blocked", $"nature d'opération non paramétrée pour le tenant « {label} » → document suspendu (jamais devinée).");

            var events = await tenant.GetEventsAsync(documentId);
            events.Should().Contain(
                e => e.Detail != null && e.Detail.Contains("nature d'opération", StringComparison.Ordinal),
                $"le motif « nature d'opération non paramétrée » est consigné dans la piste d'audit du tenant « {label} ».");
        }
    }

    [Fact]
    public async Task MappedRegime_With_OperationCategory_Reaches_ReadyToSend_AsControl()
    {
        // Témoin : régime mappé + nature d'opération renseignée → ReadyToSend. Garantit que les blocages
        // ci-dessus sont SPÉCIFIQUES au paramétrage manquant (régime non mappé / nature absente) et non un
        // blocage générique du chemin (pas de faux blocage — « bloquer plutôt qu'envoyer faux », jamais
        // « bloquer tout par défaut »).
        var (tenant, label) = _fixture.Tenants[0];
        var sourceReference = $"no_ba=b2c02-control-{label}";
        var pivot = CheckIntegrationFixtures.BuildPivot(sourceReference, regimeCode: "NORMAL");

        var documentId = await IngestAndCheckAsync(tenant, pivot);

        (await tenant.GetDocumentStateAsync(documentId)).Should().Be(
            "ReadyToSend", $"régime mappé + nature d'opération renseignée pour le tenant « {label} » : aucun blocage.");
    }

    private static async Task<Guid> IngestAndCheckAsync(PipelineE2ETenant tenant, PivotDocumentDto pivot)
    {
        var payloadHash = PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

        (await tenant.IngestAsync(pivot)).Should().Be(
            DocumentPushStatus.Accepted, "l'ingestion ne porte aucune logique métier : elle range le document en Detected.");

        var documentId = await tenant.ResolveDocumentIdAsync(pivot.SourceReference, payloadHash);
        documentId.Should().NotBeNull("l'ingestion a rangé le document en Detected.");

        await tenant.RunCheckAsync(documentId!.Value, pivot.SourceReference, payloadHash);
        return documentId.Value;
    }

    /// <summary>
    /// Deux tenants, chacun sur sa PROPRE base PostgreSQL réelle (database-per-tenant) : prouve le pilotage par
    /// le paramétrage fiscal et le blocage sûr sur ≥ 2 bases, avec isolation cross-base (CLAUDE.md n°9).
    /// </summary>
    public sealed class MultiTenantFixture : IAsyncLifetime
    {
        private readonly PipelineE2ETenant _tenantA = new("b2c02-tenant-a");
        private readonly PipelineE2ETenant _tenantB = new("b2c02-tenant-b");

        public IReadOnlyList<(PipelineE2ETenant Tenant, string Label)> Tenants { get; private set; } =
            Array.Empty<(PipelineE2ETenant, string)>();

        public async Task InitializeAsync()
        {
            await Task.WhenAll(_tenantA.InitializeAsync(), _tenantB.InitializeAsync());
            Tenants = new[] { (_tenantA, "A"), (_tenantB, "B") };
        }

        public async Task DisposeAsync()
        {
            await _tenantA.DisposeAsync();
            await _tenantB.DisposeAsync();
        }
    }
}
