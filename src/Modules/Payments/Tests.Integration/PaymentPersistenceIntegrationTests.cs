namespace Liakont.Modules.Payments.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Xunit;

/// <summary>
/// Persistance du module Payments sur PostgreSQL réel (Testcontainers, item TRK04) : round-trip decimal SANS
/// PERTE des montants (colonnes NUMERIC, CLAUDE.md n°1), idempotence de l'enregistrement, et fidélité de
/// l'agrégat (montants + taux, montants négatifs des rectificatifs — F09 §5.4).
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PaymentPersistenceIntegrationTests
{
    private readonly Fixtures.PaymentsDatabaseFixture _fixture;

    public PaymentPersistenceIntegrationTests(Fixtures.PaymentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("0.30")] // 0.1 + 0.2 — piège des flottants
    [InlineData("1162.80")]
    [InlineData("0.01")]
    [InlineData("-50.00")] // remboursement (F09 §5.4)
    public async Task Payment_Round_Trips_Decimal_Amount_Without_Loss(string amount)
    {
        var harness = new PaymentsHarness(_fixture);
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var payment = PaymentTestData.NewPayment(amount: value);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.SavePaymentAsync(payment)).Should().BeTrue();
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetPaymentByIdAsync(payment.Id);
        dto.Should().NotBeNull();
        dto!.Amount.Should().Be(value, "le montant est relu exactement (NUMERIC, jamais flottant).");
        dto.PaymentDate.Should().Be(PaymentTestData.Day);
    }

    [Fact]
    public async Task SavePayment_Is_Idempotent_On_Id()
    {
        var harness = new PaymentsHarness(_fixture);
        var payment = PaymentTestData.NewPayment();

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.SavePaymentAsync(payment)).Should().BeTrue();
            await uow.CommitAsync();
        }

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.SavePaymentAsync(payment)).Should().BeFalse("un re-push du même paiement n'insère pas un doublon.");
            await uow.CommitAsync();
        }

        (await CountPaymentsAsync(harness, payment.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Aggregate_Round_Trips_Amounts_And_Rate_Without_Loss()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregate = PaymentTestData.NewAggregate(taxableBase: -100.00m, vatAmount: -20.00m, vatRate: 0.0550m);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.CreateAggregateAsync(aggregate, PaymentAggregateEvent.Genesis(aggregate.Id, PaymentTestData.ReceivedAt)))
                .Should().BeTrue();
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetAggregateByIdAsync(aggregate.Id);
        dto.Should().NotBeNull();
        dto!.TaxableBase.Should().Be(-100.00m, "un rectificatif négatif est relu exactement (F09 §5.4).");
        dto.VatAmount.Should().Be(-20.00m);
        dto.VatRate.Should().Be(0.0550m, "le taux 5,5 % est relu sans perte (NUMERIC(6,4)).");
        dto.State.Should().Be(nameof(PaymentAggregateState.Calculated));

        // La genèse a bien été écrite atomiquement avec l'agrégat.
        var events = await harness.Queries.GetAggregateEventsAsync(aggregate.Id);
        events.Should().ContainSingle();
        events[0].EventType.Should().Be(nameof(PaymentAggregateEventType.AggregateCalculated));
    }

    [Fact]
    public async Task CreateAggregate_Is_Idempotent_On_Id()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregate = PaymentTestData.NewAggregate();

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.CreateAggregateAsync(aggregate, PaymentAggregateEvent.Genesis(aggregate.Id, PaymentTestData.ReceivedAt)))
                .Should().BeTrue();
            await uow.CommitAsync();
        }

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.CreateAggregateAsync(aggregate, PaymentAggregateEvent.Genesis(aggregate.Id, PaymentTestData.ReceivedAt)))
                .Should().BeFalse("un agrégat déjà créé n'est ni réinséré ni re-journalisé.");
            await uow.CommitAsync();
        }

        var events = await harness.Queries.GetAggregateEventsAsync(aggregate.Id);
        events.Should().ContainSingle("aucun événement de genèse dupliqué.");
    }

    [Fact]
    public async Task CreateAggregate_Rejects_GenesisEvent_With_Mismatched_AggregateId()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregateA = PaymentTestData.NewAggregate();
        var aggregateB = PaymentTestData.NewAggregate();
        var genesisMisrouted = PaymentAggregateEvent.Genesis(aggregateA.Id, PaymentTestData.ReceivedAt);

        await using var uow = await harness.UowFactory.BeginAsync();
        var act = () => uow.CreateAggregateAsync(aggregateB, genesisMisrouted);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("genesisEvent");
    }

    private static async Task<long> CountPaymentsAsync(PaymentsHarness harness, Guid id)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM payments.payments WHERE id = @id",
            new { id });
    }
}
