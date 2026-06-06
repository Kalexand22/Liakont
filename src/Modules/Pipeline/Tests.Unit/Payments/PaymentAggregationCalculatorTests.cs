namespace Liakont.Modules.Pipeline.Tests.Unit.Payments;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Xunit;

/// <summary>
/// Tests du cœur PUR de l'agrégation de paiement (PIP03a, F09 §2) : ventilation jour×taux des documents
/// mono-catégorie, suspension/non requis selon le paramétrage fiscal, et écart des cas non sourcés (Mixte,
/// taux non résolu). AUCUNE règle fiscale n'est inventée — les cas ambigus sont suspendus, pas devinés.
/// </summary>
public sealed class PaymentAggregationCalculatorTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 1);
    private static readonly DateOnly Day2 = new(2026, 6, 2);

    [Fact]
    public void Aggregates_Mono_Category_Service_By_Day_And_Rate()
    {
        var payments = new List<ResolvedPayment>
        {
            // Jour 1 : un document à deux taux, payé en totalité (couverture = 1).
            Service(Day1, 175.00m, Line(20m, 100.00m, 20.00m), Line(10m, 50.00m, 5.00m)),

            // Jour 2 : un autre document à 20 %, payé en totalité.
            Service(Day2, 60.00m, Line(20m, 50.00m, 10.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Exclusions.Should().BeEmpty();
        result.Aggregates.Should().HaveCount(3);
        result.Aggregates.Should().AllSatisfy(a => a.Status.Should().Be(PaymentAggregationStatus.Calculated));

        // Ordonné par jour puis taux croissant.
        result.Aggregates[0].Should().BeEquivalentTo(new { Date = Day1, Rate = 10m, TaxableBase = 50.00m, VatAmount = 5.00m });
        result.Aggregates[1].Should().BeEquivalentTo(new { Date = Day1, Rate = 20m, TaxableBase = 100.00m, VatAmount = 20.00m });
        result.Aggregates[2].Should().BeEquivalentTo(new { Date = Day2, Rate = 20m, TaxableBase = 50.00m, VatAmount = 10.00m });
    }

    [Fact]
    public void Sums_Same_Day_And_Rate_Across_Documents()
    {
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)),
            Service(Day1, 60.00m, Line(20m, 50.00m, 10.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().ContainSingle();
        result.Aggregates[0].TaxableBase.Should().Be(150.00m);
        result.Aggregates[0].VatAmount.Should().Be(30.00m);
    }

    [Fact]
    public void Partial_Payment_Is_Prorated_By_Coverage()
    {
        // Couverture = 87.50 / 175.00 = 0.5 (paiement partiel imputé à sa date — F09 §5.4).
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, 87.50m, Line(20m, 100.00m, 20.00m), Line(10m, 50.00m, 5.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates[0].Should().BeEquivalentTo(new { Rate = 10m, TaxableBase = 25.00m, VatAmount = 2.50m });
        result.Aggregates[1].Should().BeEquivalentTo(new { Rate = 20m, TaxableBase = 50.00m, VatAmount = 10.00m });
    }

    [Fact]
    public void Partial_Payment_Rounds_Half_Up_To_Two_Decimals()
    {
        // Couverture = 100 / 120 = 0.8333… → 83.333… → 83.33 ; 16.666… → 16.67 (half-up, CLAUDE.md n°1).
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, 100.00m, Line(20m, 100.00m, 20.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates[0].TaxableBase.Should().Be(83.33m);
        result.Aggregates[0].VatAmount.Should().Be(16.67m);
    }

    [Fact]
    public void Refund_Produces_Negative_Aggregate()
    {
        // Remboursement (montant négatif) — F09 §5.4 / INV-PAYMENTS-003.
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, -120.00m, Line(20m, 100.00m, 20.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates[0].TaxableBase.Should().Be(-100.00m);
        result.Aggregates[0].VatAmount.Should().Be(-20.00m);
    }

    [Fact]
    public void Mixte_Document_Is_Suspended_Not_Aggregated()
    {
        // Découpage frais/adjudication non sourcé (D-b) → suspendu, jamais deviné (CLAUDE.md n°2).
        var payments = new List<ResolvedPayment>
        {
            Payment(Day1, 120.00m, OperationCategory.Mixte, Line(20m, 100.00m, 20.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().BeEmpty();
        result.Exclusions.Should().ContainSingle()
            .Which.Reason.Should().Be(PaymentExclusionReason.MixteSuspended);
    }

    [Fact]
    public void Goods_Document_Is_Not_Applicable()
    {
        var payments = new List<ResolvedPayment>
        {
            Payment(Day1, 120.00m, OperationCategory.LivraisonBiens, Line(20m, 100.00m, 20.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().BeEmpty();
        result.Exclusions.Should().ContainSingle()
            .Which.Reason.Should().Be(PaymentExclusionReason.GoodsNotApplicable);
    }

    [Fact]
    public void Unresolved_Rate_Suspends_The_Document()
    {
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, 120.00m, Line(null, 100.00m, 20.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().BeEmpty();
        result.Exclusions.Should().ContainSingle()
            .Which.Reason.Should().Be(PaymentExclusionReason.UnresolvedRate);
    }

    [Fact]
    public void Zero_Total_Document_Is_Excluded()
    {
        var payments = new List<ResolvedPayment>
        {
            Service(Day1, 0.00m, Line(20m, 0.00m, 0.00m)),
        };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().BeEmpty();
        result.Exclusions.Should().ContainSingle()
            .Which.Reason.Should().Be(PaymentExclusionReason.ZeroDocumentTotal);
    }

    [Fact]
    public void Missing_FeeImputationMethod_Suspends_But_Still_Computes()
    {
        // null = jamais de prorata par défaut (F09 §5.2) : on calcule pour la traçabilité, on suspend.
        var fiscal = HappyFiscal() with { HasFeeImputationMethod = false };
        var payments = new List<ResolvedPayment> { Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)) };

        var result = PaymentAggregationCalculator.Aggregate(payments, fiscal, paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().ContainSingle();
        result.Aggregates[0].Status.Should().Be(PaymentAggregationStatus.Suspended);
        result.Aggregates[0].Reason.Should().Be(PaymentAggregationCalculator.FeeMethodMissingReason);
        result.Aggregates[0].TaxableBase.Should().Be(100.00m);
    }

    [Theory]
    [InlineData(true, false)] // fréquence déclarative manquante
    [InlineData(false, true)] // catégorie d'opération manquante
    public void Missing_Fiscal_Parameter_Suspends_But_Still_Computes(bool hasCategory, bool hasFrequency)
    {
        var fiscal = HappyFiscal() with { HasOperationCategory = hasCategory, HasReportingFrequency = hasFrequency };
        var payments = new List<ResolvedPayment> { Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)) };

        var result = PaymentAggregationCalculator.Aggregate(payments, fiscal, paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().ContainSingle();
        result.Aggregates[0].Status.Should().Be(PaymentAggregationStatus.Suspended);
        result.Aggregates[0].Reason.Should().Be(PaymentAggregationCalculator.FiscalPendingReason);
    }

    [Fact]
    public void Null_VatOnDebits_Suspends()
    {
        var fiscal = HappyFiscal() with { VatOnDebits = null };
        var payments = new List<ResolvedPayment> { Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)) };

        var result = PaymentAggregationCalculator.Aggregate(payments, fiscal, paSupportsDomesticPaymentReporting: true);

        result.Aggregates[0].Status.Should().Be(PaymentAggregationStatus.Suspended);
    }

    [Fact]
    public void VatOnDebits_True_Marks_NotRequired_But_Still_Computes()
    {
        // TVA sur les débits = exigibilité à la facturation → non requis, mais calculé pour la traçabilité (F09 §6).
        var fiscal = HappyFiscal() with { VatOnDebits = true };
        var payments = new List<ResolvedPayment> { Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)) };

        var result = PaymentAggregationCalculator.Aggregate(payments, fiscal, paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().ContainSingle();
        result.Aggregates[0].Status.Should().Be(PaymentAggregationStatus.NotRequired);
        result.Aggregates[0].TaxableBase.Should().Be(100.00m);
    }

    [Fact]
    public void Missing_Pa_Capability_Marks_Pending()
    {
        var payments = new List<ResolvedPayment> { Service(Day1, 120.00m, Line(20m, 100.00m, 20.00m)) };

        var result = PaymentAggregationCalculator.Aggregate(payments, HappyFiscal(), paSupportsDomesticPaymentReporting: false);

        result.Aggregates.Should().ContainSingle();
        result.Aggregates[0].Status.Should().Be(PaymentAggregationStatus.PendingCapability);
    }

    [Fact]
    public void Empty_Payments_Produces_No_Aggregates()
    {
        var result = PaymentAggregationCalculator.Aggregate(new List<ResolvedPayment>(), HappyFiscal(), paSupportsDomesticPaymentReporting: true);

        result.Aggregates.Should().BeEmpty();
        result.Exclusions.Should().BeEmpty();
    }

    private static PaymentFiscalContext HappyFiscal() => new()
    {
        VatOnDebits = false,
        HasOperationCategory = true,
        HasReportingFrequency = true,
        HasFeeImputationMethod = true,
    };

    private static VentilationLine Line(decimal? rate, decimal taxableBase, decimal vatAmount) =>
        VentilationLine.Create(rate, taxableBase, vatAmount);

    private static ResolvedPayment Service(DateOnly date, decimal amount, params VentilationLine[] lines) =>
        Payment(date, amount, OperationCategory.PrestationServices, lines);

    private static ResolvedPayment Payment(DateOnly date, decimal amount, OperationCategory category, params VentilationLine[] lines) =>
        new()
        {
            Date = date,
            Amount = amount,
            RelatedDocumentNumber = "F-2026-0001",
            Document = new DocumentVentilation { OperationCategory = category, Lines = lines.ToList() },
        };
}
