namespace Liakont.Modules.TenantSettings.Tests.Integration;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Queries;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// <see cref="SetAlertRoutingMatrixHandler"/> + <see cref="PostgresAlertRoutingQueries"/> (FIX212, F12 §5.3.1) :
/// la matrice est remplacée EN BLOC, relue ordonnée par rang, journalisée (entité <c>AlertRoutingMatrix</c>,
/// scopée par société), et une liste vide efface la matrice (retour au modèle simple par défaut).
/// </summary>
[Collection("TenantSettingsIntegration")]
public sealed class SetAlertRoutingMatrixIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public SetAlertRoutingMatrixIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Replaces_The_Whole_Matrix_And_Reads_It_Back_Ordered()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var routing = new PostgresAlertRoutingQueries(harness.ConnectionFactory);
        var handler = new SetAlertRoutingMatrixHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetAlertRoutingMatrixCommand
            {
                Rules =
                [
                    new AlertRoutingRuleInput { RuleKey = "documents.pa_rejected", Recipients = ["compta@acme.test"] },
                    new AlertRoutingRuleInput { Severity = "Critical", Recipients = ["it@acme.test", "admin@acme.test"] },
                ],
            },
            CancellationToken.None);

        var matrix = await routing.GetAlertRoutingMatrix(harness.CompanyId);

        matrix.Should().HaveCount(2);
        matrix[0].RuleKey.Should().Be("documents.pa_rejected");
        matrix[0].Severity.Should().BeNull();
        matrix[0].Ordinal.Should().Be(0);
        matrix[1].Severity.Should().Be("Critical");
        matrix[1].Recipients.Should().Equal("it@acme.test", "admin@acme.test");
        matrix[1].Ordinal.Should().Be(1);
    }

    [Fact]
    public async Task Replacing_Supersedes_The_Previous_Entries()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var routing = new PostgresAlertRoutingQueries(harness.ConnectionFactory);
        var handler = new SetAlertRoutingMatrixHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetAlertRoutingMatrixCommand
            {
                Rules =
                [
                    new AlertRoutingRuleInput { RuleKey = "agent.mute", Recipients = ["ancien@acme.test"] },
                    new AlertRoutingRuleInput { Severity = "Warning", Recipients = ["autre@acme.test"] },
                ],
            },
            CancellationToken.None);

        await handler.Handle(
            new SetAlertRoutingMatrixCommand
            {
                Rules = [new AlertRoutingRuleInput { RuleKey = "documents.blocked", Recipients = ["nouveau@acme.test"] }],
            },
            CancellationToken.None);

        var matrix = await routing.GetAlertRoutingMatrix(harness.CompanyId);

        matrix.Should().ContainSingle();
        matrix[0].RuleKey.Should().Be("documents.blocked");
        matrix[0].Recipients.Should().Equal("nouveau@acme.test");
    }

    [Fact]
    public async Task Empty_List_Clears_The_Matrix()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var routing = new PostgresAlertRoutingQueries(harness.ConnectionFactory);
        var handler = new SetAlertRoutingMatrixHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetAlertRoutingMatrixCommand
            {
                Rules = [new AlertRoutingRuleInput { Severity = "Critical", Recipients = ["compta@acme.test"] }],
            },
            CancellationToken.None);

        await handler.Handle(new SetAlertRoutingMatrixCommand { Rules = [] }, CancellationToken.None);

        var matrix = await routing.GetAlertRoutingMatrix(harness.CompanyId);
        matrix.Should().BeEmpty();
    }

    [Fact]
    public async Task Journals_The_Update_Scoped_To_The_Company()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var handler = new SetAlertRoutingMatrixHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        await handler.Handle(
            new SetAlertRoutingMatrixCommand
            {
                Rules = [new AlertRoutingRuleInput { RuleKey = "agent.mute", Recipients = ["secret@acme.test"] }],
            },
            CancellationToken.None);

        // Journalisée (piste append-only) avec l'identité de l'opérateur ; la capture ne porte ni la
        // description ni les adresses (le handler ne consigne que le NOMBRE d'entrées — INV-005/n°10).
        harness.ActivityLogger.Entries.Should().Contain(e =>
            e.EntityType == "AlertRoutingMatrix"
            && e.ActivityType == "updated"
            && e.ActorId == harness.UserId.ToString()
            && e.CompanyId == harness.CompanyId);
    }
}
