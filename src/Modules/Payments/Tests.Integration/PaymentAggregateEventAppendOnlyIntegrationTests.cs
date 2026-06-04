namespace Liakont.Modules.Payments.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Npgsql;
using Xunit;

/// <summary>
/// La piste d'audit <c>payments.payment_aggregate_events</c> est APPEND-ONLY au niveau base (CLAUDE.md n°4) :
/// des triggers rejettent tout UPDATE/DELETE d'une entrée et tout TRUNCATE de la table — même discipline et
/// même valeur fiscale que <c>document_events</c> (un agrégat transmis à la DGFiP est une donnée d'audit).
/// Vérifié EN DIRECT sur PostgreSQL réel : la garantie ne dépend pas du code applicatif.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PaymentAggregateEventAppendOnlyIntegrationTests
{
    private readonly Fixtures.PaymentsDatabaseFixture _fixture;

    public PaymentAggregateEventAppendOnlyIntegrationTests(Fixtures.PaymentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Update_Of_An_Event_Is_Rejected()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregateId = await SeedAggregateWithGenesisAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var update = async () => await conn.ExecuteAsync(
            "UPDATE payments.payment_aggregate_events SET detail = 'altéré' WHERE aggregate_id = @a",
            new { a = aggregateId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        (await EventCountAsync(harness, aggregateId)).Should().Be(1, "l'UPDATE a été rejeté.");
    }

    [Fact]
    public async Task Delete_Of_An_Event_Is_Rejected()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregateId = await SeedAggregateWithGenesisAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM payments.payment_aggregate_events WHERE aggregate_id = @a",
            new { a = aggregateId });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        (await EventCountAsync(harness, aggregateId)).Should().Be(1, "le DELETE a été rejeté.");
    }

    [Fact]
    public async Task Truncate_Of_The_Audit_Table_Is_Rejected()
    {
        var harness = new PaymentsHarness(_fixture);
        var aggregateId = await SeedAggregateWithGenesisAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE payments.payment_aggregate_events");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        (await EventCountAsync(harness, aggregateId)).Should().Be(1, "le TRUNCATE de masse a été rejeté.");
    }

    private static async Task<Guid> SeedAggregateWithGenesisAsync(PaymentsHarness harness)
    {
        var aggregate = PaymentTestData.NewAggregate();
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateAggregateAsync(aggregate, PaymentAggregateEvent.Genesis(aggregate.Id, PaymentTestData.ReceivedAt));
        await uow.CommitAsync();
        return aggregate.Id;
    }

    private static async Task<long> EventCountAsync(PaymentsHarness harness, Guid aggregateId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM payments.payment_aggregate_events WHERE aggregate_id = @a",
            new { a = aggregateId });
    }
}
