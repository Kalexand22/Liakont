namespace Liakont.Console.Api.Tests.Integration;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Contracts;
using Xunit;

/// <summary>
/// Test d'intégration du handler système du déclencheur MONO-TENANT <see cref="SendTenantTrigger"/> (API02a,
/// ADR-0016). Il PROUVE l'exécution réelle et tenant-scopée — au-delà du seul 202 d'un endpoint : le handler,
/// exécuté comme le ferait le <c>JobWorker</c>, rétablit le SEUL tenant cible via <c>ITenantScopeFactory.Create</c>
/// et déroule le SEND (trace <c>pipeline.run_logs</c> run_type=Send, run_trigger=Manual) dans la base de CE
/// tenant — et d'AUCUN autre (INV-API02a-1/-3/-5 ; anti faux-vert).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class SendTenantTriggerHandlerIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public SendTenantTriggerHandlerIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Handler_Runs_Send_For_Target_Tenant_Only()
    {
        var beforeA = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<SendTenantTrigger>>();
            await handler.HandleAsync(new SendTenantTrigger(ConsoleApiFactory.TenantA, DryRun: false));
        }

        var afterA = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA);
        afterA.Should().BeGreaterThan(beforeA, "le SEND manuel s'est exécuté dans la base du tenant cible (exécution réelle)");
        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantB))
            .Should().Be(0, "aucun SEND manuel n'a été déclenché pour le tenant B (isolation cross-tenant)");
    }

    [Fact]
    public async Task Handler_DryRun_Also_Targets_Single_Tenant()
    {
        var beforeA = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<SendTenantTrigger>>();
            await handler.HandleAsync(new SendTenantTrigger(ConsoleApiFactory.TenantA, DryRun: true));
        }

        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA))
            .Should().BeGreaterThan(beforeA, "même en simulation, une trace d'exécution est écrite pour le tenant cible");
        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantB))
            .Should().Be(0, "la simulation reste mono-tenant");
    }
}
