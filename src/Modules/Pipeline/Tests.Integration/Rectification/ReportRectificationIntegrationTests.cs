namespace Liakont.Modules.Pipeline.Tests.Integration.Rectification;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Domain.Rectification;
using Liakont.Modules.Pipeline.Infrastructure.Rectification;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Npgsql;
using Xunit;

/// <summary>
/// Rectification d'e-reporting (PIP04, flux RE annule-et-remplace — F07-F08 §B.1) bout en bout sur une base
/// tenant PostgreSQL réelle. Couvre les déclencheurs (rectificatif manuel, avoir sur période déclarée),
/// l'absence de capacité PA (en attente, jamais envoyé), l'idempotence (double déclenchement = une seule
/// transmission), l'historique append-only et la ré-évaluation par le job tenant. Base partagée : chaque test
/// utilise une PÉRIODE distincte et fixe explicitement les capacités (pas de dépendance à l'ordre des tests).
/// </summary>
public sealed class ReportRectificationIntegrationTests : IClassFixture<RectificationHarness>
{
    private const string SendPaymentReport = "SendPaymentReportAsync";

    private readonly RectificationHarness _harness;

    public ReportRectificationIntegrationTests(RectificationHarness harness) => _harness = harness;

    [Fact]
    public async Task Manual_Rectification_Transmits_And_Records_History()
    {
        var (start, end) = Period(1);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[]
        {
            Calc(new DateOnly(2026, 1, 5), 20m, 100.00m, 20.00m),
            Calc(new DateOnly(2026, 1, 6), 10m, 50.00m, 5.00m),
        });

        var outcome = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        outcome.Decision.Should().Be(ReportRectificationDecision.Transmitted);
        outcome.Rectification!.Lines.Should().HaveCount(2, "le rectificatif porte l'agrégat COMPLET de la période (annule-et-remplace).");
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(1);

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().ContainSingle();
        history[0].Status.Should().Be(ReportRectificationStatus.Transmitted);
        history[0].PayloadSnapshot.Should().Contain("100");
    }

    [Fact]
    public async Task Avoir_On_Declared_Period_Produces_Replacement_Rectificatif()
    {
        var (start, end) = Period(2);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);

        // Déclaration initiale de la période.
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 2, 10), 20m, 200.00m, 40.00m) });
        var initial = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        initial.Decision.Should().Be(ReportRectificationDecision.Transmitted);

        // Un avoir réduit la base encaissée de la période (F09 §5.4) → l'agrégat corrigé change.
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 2, 10), 20m, 150.00m, 30.00m) });
        var rectified = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        rectified.Decision.Should().Be(ReportRectificationDecision.Transmitted);

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().HaveCount(2, "l'historique conserve l'initial ET le rectificatif (append-only, jamais effacé).");
        history[0].ContentHash.Should().NotBe(history[1].ContentHash, "annule-et-remplace : le contenu corrigé diffère de l'initial.");
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(2);
    }

    [Fact]
    public async Task Pa_Without_Rectification_Capability_Is_Pending_Never_Sent()
    {
        var (start, end) = Period(3);

        // Capacité de rectification ABSENTE (même si le e-reporting de paiement est, lui, supporté).
        _harness.SetCapabilities(supportsRectification: false, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 3, 12), 20m, 100.00m, 20.00m) });

        var outcome = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        outcome.Decision.Should().Be(ReportRectificationDecision.PendingCapability);
        _harness.PaClient.Calls.Should().NotContain(c => c.Method == SendPaymentReport, "aucun envoi à l'aveugle quand la capacité de rectification est absente.");

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().ContainSingle();
        history[0].Status.Should().Be(ReportRectificationStatus.PendingCapability);
    }

    [Fact]
    public async Task Identical_Retrigger_Is_Idempotent()
    {
        var (start, end) = Period(4);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 4, 8), 20m, 80.00m, 16.00m) });

        var first = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        var second = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        first.Decision.Should().Be(ReportRectificationDecision.Transmitted);
        second.Decision.Should().Be(ReportRectificationDecision.NoChange, "un double déclenchement au contenu identique ne re-transmet pas (PIP04 §4).");
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(1, "une seule transmission pour un contenu identique.");

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().ContainSingle("aucune entrée de journal ajoutée pour un rectificatif identique.");
    }

    [Fact]
    public async Task Rectification_Ledger_Is_Append_Only()
    {
        var (start, end) = Period(5);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 5, 3), 20m, 100.00m, 20.00m) });
        await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        var startLiteral = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var update = async () => await _harness.ExecuteRawAsync(
            $"UPDATE pipeline.report_rectifications SET detail = 'tamper' WHERE period_start = '{startLiteral}'");
        var delete = async () => await _harness.ExecuteRawAsync(
            $"DELETE FROM pipeline.report_rectifications WHERE period_start = '{startLiteral}'");

        await update.Should().ThrowAsync<PostgresException>("le journal des rectificatifs est append-only (trigger base, CLAUDE.md n°4).");
        await delete.Should().ThrowAsync<PostgresException>("aucune suppression d'une entrée du journal append-only.");
    }

    [Fact]
    public async Task Tenant_Job_Reevaluates_Declared_Period_On_Aggregate_Change()
    {
        var (start, end) = Period(6);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);

        // Période déclarée puis corrigée (avoir / altération source en amont = changement d'agrégat).
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 6, 14), 20m, 300.00m, 60.00m) });
        await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 6, 14), 20m, 250.00m, 50.00m) });

        await _harness.RunRectifyJobAsync();

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().HaveCount(2, "le job ré-évalue la période déclarée et émet le rectificatif sur changement d'agrégat.");
        history[^1].Status.Should().Be(ReportRectificationStatus.Transmitted);
        (await _harness.CountRunLogsAsync(PipelineRunType.Rectify)).Should().BeGreaterThan(0, "une trace d'exécution Rectify est écrite.");
    }

    [Fact]
    public async Task Pending_Capability_Transmits_Automatically_On_Activation()
    {
        var (start, end) = Period(7);
        _harness.SetCapabilities(supportsRectification: false, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 7, 9), 20m, 100.00m, 20.00m) });

        var pending = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        pending.Decision.Should().Be(ReportRectificationDecision.PendingCapability);

        // Activation de la capacité de rectification ⇒ le rectificatif en attente part automatiquement (INV-PIPELINE-034).
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true);
        var transmitted = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        transmitted.Decision.Should().Be(ReportRectificationDecision.Transmitted);
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(1, "journal réinitialisé à l'activation ; un seul envoi depuis.");

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().HaveCount(2);
        history[^1].Status.Should().Be(ReportRectificationStatus.Transmitted);
    }

    [Fact]
    public async Task Pa_Rejection_Is_Recorded_And_Not_Auto_Retried()
    {
        var (start, end) = Period(8);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true, FakePaScenario.Rejected);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 8, 4), 20m, 90.00m, 18.00m) });

        var rejected = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        rejected.Decision.Should().Be(ReportRectificationDecision.RejectedByPa);

        // Même contenu rejeté : pas de retry automatique (l'opérateur doit corriger).
        var retry = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        retry.Decision.Should().Be(ReportRectificationDecision.NoChange);
        retry.Detail.Should().Contain("REJETÉ", "le message idempotent reflète le dernier état (rejet), pas « aucun changement bénin ».");
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(1, "un rejet métier identique n'est pas renvoyé automatiquement.");
    }

    [Fact]
    public async Task Technical_Error_Is_Retried_On_Next_Cycle()
    {
        var (start, end) = Period(9);
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: true, FakePaScenario.TechnicalError);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 9, 6), 20m, 70.00m, 14.00m) });

        var first = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        var second = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        first.Decision.Should().Be(ReportRectificationDecision.TechnicalError);
        second.Decision.Should().Be(ReportRectificationDecision.TechnicalError, "une erreur technique au contenu identique est re-tentée (transitoire).");
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(2);
    }

    [Fact]
    public async Task Payment_Reporting_Capability_Absent_Maps_To_Pending()
    {
        var (start, end) = Period(10);

        // Capacité de RECTIFICATION présente, mais e-reporting de paiement (10.4) absent : l'envoi est tenté
        // puis la PA répond « capacité absente » (résultat typé) ⇒ en attente, jamais Transmitted ni exception.
        _harness.SetCapabilities(supportsRectification: true, supportsDomesticPaymentReporting: false);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 10, 3), 20m, 60.00m, 12.00m) });

        var outcome = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        outcome.Decision.Should().Be(ReportRectificationDecision.PendingCapability);
        _harness.PaClient.Calls.Count(c => c.Method == SendPaymentReport).Should().Be(1);

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history[^1].Status.Should().Be(ReportRectificationStatus.PendingCapability);
    }

    [Fact]
    public async Task Still_Incapable_Retrigger_Is_Idempotent()
    {
        var (start, end) = Period(11);
        _harness.SetCapabilities(supportsRectification: false, supportsDomesticPaymentReporting: true);
        await _harness.SeedAggregatesAsync(new[] { Calc(new DateOnly(2026, 11, 7), 20m, 40.00m, 8.00m) });

        var first = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);
        var second = await _harness.RectifyAsync(PaymentReportFlux.Domestic, start, end);

        first.Decision.Should().Be(ReportRectificationDecision.PendingCapability);
        second.Decision.Should().Be(ReportRectificationDecision.NoChange, "capacité toujours absente + contenu identique = pas de doublon d'entrée.");

        var history = await _harness.GetHistoryAsync(PaymentReportFlux.Domestic, start, end);
        history.Should().ContainSingle();
    }

    private static (DateOnly Start, DateOnly End) Period(int month) =>
        (new DateOnly(2026, month, 1), new DateOnly(2026, month, 28));

    private static PaymentDailyAggregate Calc(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount) =>
        new()
        {
            Date = date,
            Rate = rate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
            Status = PaymentAggregationStatus.Calculated,
            Reason = null,
        };
}
