namespace Liakont.Modules.Pipeline.Tests.Unit.Rectification;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Domain.Rectification;
using Xunit;

/// <summary>
/// Tests du cœur PUR de la reconstruction d'un rectificatif (PIP04, flux RE — F07-F08 §B.1) : annule-et-remplace
/// COMPLET (toutes les lignes reportables de la période, pas un delta), empreinte de contenu DÉTERMINISTE
/// (idempotence) et insensible à l'échelle décimale. Aucune règle fiscale inventée : le builder ne fait que
/// re-sommer/filtrer les agrégats existants.
/// </summary>
public sealed class RectificationBuilderTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly DateOnly End = new(2026, 6, 30);

    [Fact]
    public void Build_Includes_All_Reportable_Lines_In_Bounds_Sorted()
    {
        var aggregates = new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 2), 20m, 100.00m, 20.00m),
            Calculated(new DateOnly(2026, 6, 1), 10m, 50.00m, 5.00m),
            Calculated(new DateOnly(2026, 6, 1), 20m, 80.00m, 16.00m),
        };

        var rectification = RectificationBuilder.Build(Start, End, aggregates);

        rectification.IsEmpty.Should().BeFalse();
        rectification.Lines.Should().HaveCount(3);

        // Trié par jour puis taux croissant — la photo complète de la période.
        rectification.Lines[0].Should().BeEquivalentTo(new { Date = new DateOnly(2026, 6, 1), Rate = 10m, TaxableBase = 50.00m, VatAmount = 5.00m });
        rectification.Lines[1].Should().BeEquivalentTo(new { Date = new DateOnly(2026, 6, 1), Rate = 20m, TaxableBase = 80.00m, VatAmount = 16.00m });
        rectification.Lines[2].Should().BeEquivalentTo(new { Date = new DateOnly(2026, 6, 2), Rate = 20m, TaxableBase = 100.00m, VatAmount = 20.00m });
    }

    [Fact]
    public void Build_Excludes_Out_Of_Bounds_And_Non_Reportable_Lines()
    {
        var aggregates = new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 15), 20m, 100.00m, 20.00m),

            // Hors bornes (mois suivant) — exclu.
            Calculated(new DateOnly(2026, 7, 1), 20m, 999.00m, 199.80m),

            // Non reportables (suspendu / non requis / en attente) — hors déclaration.
            Suspended(new DateOnly(2026, 6, 10), 20m, 10.00m, 2.00m),
            NotRequired(new DateOnly(2026, 6, 11), 20m, 11.00m, 2.20m),
            Pending(new DateOnly(2026, 6, 12), 20m, 12.00m, 2.40m),
        };

        var rectification = RectificationBuilder.Build(Start, End, aggregates);

        rectification.Lines.Should().ContainSingle();
        rectification.Lines[0].Date.Should().Be(new DateOnly(2026, 6, 15));
        rectification.Lines[0].TaxableBase.Should().Be(100.00m);
    }

    [Fact]
    public void ContentHash_Is_Stable_Regardless_Of_Input_Order_And_Decimal_Scale()
    {
        var first = RectificationBuilder.Build(Start, End, new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 1), 20m, 100.00m, 20.00m),
            Calculated(new DateOnly(2026, 6, 2), 10m, 50.0m, 5.0m),
        });

        // Mêmes valeurs LOGIQUES, ordre différent, échelle décimale différente (20.000 == 20.00 == 20).
        var second = RectificationBuilder.Build(Start, End, new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 2), 10.000m, 50.00m, 5.00m),
            Calculated(new DateOnly(2026, 6, 1), 20.0m, 100.000m, 20.0m),
        });

        second.ContentHash.Should().Be(first.ContentHash);
        first.ContentHash.Should().HaveLength(64);
    }

    [Fact]
    public void ContentHash_Changes_When_A_Line_Is_Corrected()
    {
        var aggregates = new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 1), 20m, 100.00m, 20.00m),
        };
        var initial = RectificationBuilder.Build(Start, End, aggregates);

        // Un avoir réduit la base encaissée du jour (F09 §5.4 : montant négatif net) → empreinte différente.
        var corrected = RectificationBuilder.Build(Start, End, new List<PaymentDailyAggregate>
        {
            Calculated(new DateOnly(2026, 6, 1), 20m, 70.00m, 14.00m),
        });

        corrected.ContentHash.Should().NotBe(initial.ContentHash);
    }

    [Fact]
    public void Build_Is_Empty_When_No_Reportable_Line()
    {
        var rectification = RectificationBuilder.Build(Start, End, new List<PaymentDailyAggregate>
        {
            Suspended(new DateOnly(2026, 6, 1), 20m, 100.00m, 20.00m),
        });

        rectification.IsEmpty.Should().BeTrue();
        rectification.Lines.Should().BeEmpty();
        rectification.ContentHash.Should().HaveLength(64);
    }

    [Fact]
    public void Build_Rejects_Period_End_Before_Start()
    {
        var act = () => RectificationBuilder.Build(End, Start, new List<PaymentDailyAggregate>());

        act.Should().Throw<ArgumentException>();
    }

    private static PaymentDailyAggregate Calculated(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount) =>
        Line(date, rate, taxableBase, vatAmount, PaymentAggregationStatus.Calculated);

    private static PaymentDailyAggregate Suspended(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount) =>
        Line(date, rate, taxableBase, vatAmount, PaymentAggregationStatus.Suspended);

    private static PaymentDailyAggregate NotRequired(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount) =>
        Line(date, rate, taxableBase, vatAmount, PaymentAggregationStatus.NotRequired);

    private static PaymentDailyAggregate Pending(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount) =>
        Line(date, rate, taxableBase, vatAmount, PaymentAggregationStatus.PendingCapability);

    private static PaymentDailyAggregate Line(DateOnly date, decimal rate, decimal taxableBase, decimal vatAmount, PaymentAggregationStatus status) =>
        new()
        {
            Date = date,
            Rate = rate,
            TaxableBase = taxableBase,
            VatAmount = vatAmount,
            Status = status,
            Reason = status == PaymentAggregationStatus.Calculated ? null : "test",
        };
}
