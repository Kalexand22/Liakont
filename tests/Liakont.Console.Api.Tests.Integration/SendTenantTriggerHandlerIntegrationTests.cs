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
/// tenant — et d'AUCUN autre (INV-API02a-1/-3/-5 ; anti faux-vert). Cible le tenant dédié
/// <see cref="ConsoleApiFactory.TenantAction"/> ; les tenants A/B ne reçoivent JAMAIS de SEND (isolation).
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
        var beforeAct = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantAction);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<SendTenantTrigger>>();
            await handler.HandleAsync(new SendTenantTrigger(ConsoleApiFactory.TenantAction, DryRun: false));
        }

        var afterAct = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantAction);
        afterAct.Should().BeGreaterThan(beforeAct, "le SEND manuel s'est exécuté dans la base du tenant cible (exécution réelle)");
        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA))
            .Should().Be(0, "aucun SEND manuel n'a été déclenché pour le tenant A (isolation cross-tenant)");
        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantB))
            .Should().Be(0, "aucun SEND manuel n'a été déclenché pour le tenant B (isolation cross-tenant)");
    }

    [Fact]
    public async Task Handler_DryRun_Also_Targets_Single_Tenant()
    {
        var beforeAct = await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantAction);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<SendTenantTrigger>>();
            await handler.HandleAsync(new SendTenantTrigger(ConsoleApiFactory.TenantAction, DryRun: true));
        }

        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantAction))
            .Should().BeGreaterThan(beforeAct, "même en simulation, une trace d'exécution est écrite pour le tenant cible");
        (await _factory.CountManualSendRunLogsAsync(ConsoleApiFactory.TenantA))
            .Should().Be(0, "la simulation reste mono-tenant (tenant A intact)");
    }
}
