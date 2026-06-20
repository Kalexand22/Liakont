namespace Liakont.Modules.Pipeline.Tests.Integration.Aggregation;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Npgsql;
using Xunit;

/// <summary>
/// Agrégation de paiement (PIP03a) bout en bout sur une base tenant PostgreSQL réelle. Prouve l'invariant
/// d'architecture d'ADR-0015 : le snapshot de ventilation écrit au CHECK SURVIT à la purge du staging, et
/// l'agrégation jour×taux reste possible APRÈS la purge. Couvre aussi la qualification fiscale réelle
/// (capacité PA absente, TVA sur les débits) câblée depuis le paramétrage persisté. Base partagée : chaque
/// test utilise une DATE distincte (la projection est clé par jour×taux) et fixe explicitement capacité +
/// paramétrage fiscal (pas de dépendance à l'ordre des tests).
/// </summary>
public sealed class PaymentAggregationIntegrationTests : IClassFixture<PaymentAggregationHarness>
{
    private readonly PaymentAggregationHarness _harness;

    public PaymentAggregationIntegrationTests(PaymentAggregationHarness harness) => _harness = harness;

    [Fact]
    public async Task Snapshot_Survives_Staging_Purge_And_Aggregation_Still_Works()
    {
        var paymentDate = new DateOnly(2026, 3, 1);
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m));

        _harness.SetPaymentReportingCapability(supported: true);
        var hash = await _harness.CheckServiceDocumentAsync(documentId, pivot);

        // Le snapshot existe après le CHECK.
        (await _harness.GetSnapshotAsync(documentId)).Should().NotBeNull();

        // Purge du staging (ADR-0014) — le contenu transitoire disparaît.
        await _harness.PurgeStagingAsync(documentId, hash);
        (await _harness.IsStagedAsync(documentId, hash)).Should().BeFalse("le staging est purgé après émission (ADR-0014).");

        // INVARIANT ADR-0015 : le snapshot SURVIT à la purge — l'agrégation reste requêtable.
        var snapshot = await _harness.GetSnapshotAsync(documentId);
        snapshot.Should().NotBeNull("le snapshot est une projection durable, distincte du staging purgé (INV-VENTILATION-006).");
        snapshot!.Lines.Should().ContainSingle();
        snapshot.Lines[0].Rate.Should().Be(20m);
        snapshot.Lines[0].TaxableBase.Should().Be(100.00m);
        snapshot.Lines[0].VatAmount.Should().Be(20.00m);

        await _harness.SetFiscalSettingsAsync(vatOnDebits: false, OperationCategory.PrestationServices, "Mensuelle", FeeImputationMethod.AgregationJourTaux);
        await _harness.SeedPaymentAsync(paymentDate, 120.00m, pivot.Number);

        await _harness.RunAggregateAsync();

        var aggregate = (await _harness.GetAggregatesAsync()).Single(a => a.Date == paymentDate);
        aggregate.Rate.Should().Be(20m);
        aggregate.TaxableBase.Should().Be(100.00m, "l'agrégation après purge décompose l'encaissement depuis le snapshot.");
        aggregate.VatAmount.Should().Be(20.00m);
        aggregate.Status.Should().Be(PaymentAggregationStatus.Calculated);
    }

    [Fact]
    public async Task Multi_Rate_Document_Aggregates_Each_Rate()
    {
        var paymentDate = new DateOnly(2026, 3, 5);
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m), (50.00m, 5.00m, 10m));

        _harness.SetPaymentReportingCapability(supported: true);
        _harness.SetSourceExposesPayments(exposes: true);
        await _harness.CheckServiceDocumentAsync(documentId, pivot);
        await _harness.SetFiscalSettingsAsync(vatOnDebits: false, OperationCategory.PrestationServices, "Mensuelle", FeeImputationMethod.AgregationJourTaux);
        await _harness.SeedPaymentAsync(paymentDate, 175.00m, pivot.Number);

        await _harness.RunAggregateAsync();

        var dayAggregates = (await _harness.GetAggregatesAsync()).Where(a => a.Date == paymentDate).OrderBy(a => a.Rate).ToList();
        dayAggregates.Should().HaveCount(2);
        dayAggregates[0].Should().BeEquivalentTo(new { Rate = 10m, TaxableBase = 50.00m, VatAmount = 5.00m });
        dayAggregates[1].Should().BeEquivalentTo(new { Rate = 20m, TaxableBase = 100.00m, VatAmount = 20.00m });
    }

    [Fact]
    public async Task Missing_Pa_Capability_Persists_Aggregate_As_Pending()
    {
        var paymentDate = new DateOnly(2026, 3, 10);
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m));

        _harness.SetPaymentReportingCapability(supported: false);
        _harness.SetSourceExposesPayments(exposes: true);
        await _harness.CheckServiceDocumentAsync(documentId, pivot);
        await _harness.SetFiscalSettingsAsync(vatOnDebits: false, OperationCategory.PrestationServices, "Mensuelle", FeeImputationMethod.AgregationJourTaux);
        await _harness.SeedPaymentAsync(paymentDate, 120.00m, pivot.Number);

        await _harness.RunAggregateAsync();

        var aggregate = (await _harness.GetAggregatesAsync()).Single(a => a.Date == paymentDate);
        aggregate.Status.Should().Be(PaymentAggregationStatus.PendingCapability, "capacité PA absente = agrégat persisté en attente, jamais perdu.");
        aggregate.TaxableBase.Should().Be(100.00m, "l'agrégat est calculé pour la traçabilité même sans capacité.");
    }

    [Fact]
    public async Task VatOnDebits_True_Persists_Aggregate_As_NotRequired()
    {
        var paymentDate = new DateOnly(2026, 3, 15);
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m));

        _harness.SetPaymentReportingCapability(supported: true);
        _harness.SetSourceExposesPayments(exposes: true);
        await _harness.CheckServiceDocumentAsync(documentId, pivot);
        await _harness.SetFiscalSettingsAsync(vatOnDebits: true, OperationCategory.PrestationServices, "Mensuelle", FeeImputationMethod.AgregationJourTaux);
        await _harness.SeedPaymentAsync(paymentDate, 120.00m, pivot.Number);

        await _harness.RunAggregateAsync();

        var aggregate = (await _harness.GetAggregatesAsync()).Single(a => a.Date == paymentDate);
        aggregate.Status.Should().Be(PaymentAggregationStatus.NotRequired, "TVA sur les débits = exigibilité à la facturation, non requis (mais calculé).");
    }

    [Fact]
    public async Task Source_Not_Exposing_Payments_Persists_Aggregate_As_SourceWithoutPayments()
    {
        // RD403 : la source ne déclare pas exposer les encaissements (ExposesPayments=false). L'agrégat reste
        // calculé pour la traçabilité, mais qualifié « source sans encaissements » — jamais transmis à tort,
        // et DISTINCT de « zéro encaissement » (qui serait Calculated). Tous les autres paramètres sont au vert.
        var paymentDate = new DateOnly(2026, 3, 20);
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m));

        _harness.SetPaymentReportingCapability(supported: true);
        _harness.SetSourceExposesPayments(exposes: false);
        await _harness.CheckServiceDocumentAsync(documentId, pivot);
        await _harness.SetFiscalSettingsAsync(vatOnDebits: false, OperationCategory.PrestationServices, "Mensuelle", FeeImputationMethod.AgregationJourTaux);
        await _harness.SeedPaymentAsync(paymentDate, 120.00m, pivot.Number);

        await _harness.RunAggregateAsync();

        var aggregate = (await _harness.GetAggregatesAsync()).Single(a => a.Date == paymentDate);
        aggregate.Status.Should().Be(PaymentAggregationStatus.SourceWithoutPayments, "source qui n'expose pas les encaissements = e-reporting de paiement non applicable (RD403).");
        aggregate.TaxableBase.Should().Be(100.00m, "l'agrégat est calculé pour la traçabilité même si la source n'expose pas les paiements.");

        // Réinitialise la capacité partagée du harnais pour ne pas affecter les autres tests (fixture partagée).
        _harness.SetSourceExposesPayments(exposes: true);
    }

    [Fact]
    public async Task Snapshot_Is_Append_Only_And_Idempotent()
    {
        var documentId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString("N");
        var pivot = AggregationFixtures.BuildServicePivot(sourceReference, (100.00m, 20.00m, 20m));

        _harness.SetPaymentReportingCapability(supported: true);
        _harness.SetSourceExposesPayments(exposes: true);
        await _harness.CheckServiceDocumentAsync(documentId, pivot);

        var snapshot = await _harness.GetSnapshotAsync(documentId);
        snapshot.Should().NotBeNull();
        snapshot!.Lines[0].Category.Should().Be("S", "la catégorie UNCL5305 est capturée pour l'exclusion sourcée (F09 §2).");

        // Idempotence : ré-écrire le même (document_id, mapping_version) n'insère pas de doublon (INV-VENTILATION-003).
        (await _harness.SaveSnapshotAsync(snapshot)).Should().BeFalse("un re-CHECK du même document ne duplique pas le snapshot.");

        // Append-only : toute mutation/suppression d'une entrée est rejetée par le trigger base (INV-VENTILATION-003).
        var update = async () => await _harness.ExecuteRawAsync(
            "UPDATE pipeline.ventilation_snapshots SET document_number = 'altéré' WHERE document_id = '" + documentId + "'");
        await update.Should().ThrowAsync<PostgresException>("le snapshot est append-only (CLAUDE.md n°4).");

        var delete = async () => await _harness.ExecuteRawAsync(
            "DELETE FROM pipeline.ventilation_snapshots WHERE document_id = '" + documentId + "'");
        await delete.Should().ThrowAsync<PostgresException>("le snapshot est append-only (CLAUDE.md n°4).");
    }
}
