namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts;
using Xunit;

/// <summary>
/// Smoke-test du graphe DI du module de flotte (OPS04) : avec les dépendances externes attendues du Host
/// (santé, registre des tenants, transport email, connexion système), <c>AddFleetSupervisionModule</c> doit
/// rendre RÉSOLVABLES les services du module — donc les job handlers du Host CONSTRUCTIBLES. Garde-fou de
/// démarrage : un service non câblé échouerait ici plutôt qu'au runtime de l'instance.
/// </summary>
public sealed class FleetSupervisionModuleRegistrationTests
{
    [Fact]
    public void Module_Services_Resolve_With_Host_Dependencies_Present()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks();

        // Dépendances fournies par le Host (composition root) — ici des doubles minimaux.
        services.AddScoped<ISystemConnectionFactory, FakeSystemConnectionFactory>();
        services.AddScoped<ITenantQueries, FakeTenantQueries>();
        services.AddScoped<IEmailTransport, FakeEmailTransport>();
        services.Configure<FleetSupervisionOptions>(_ => { });

        services.AddFleetSupervisionModule();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        sp.GetRequiredService<IFleetHeartbeatIngestor>().Should().NotBeNull();
        sp.GetRequiredService<IFleetQueries>().Should().NotBeNull();
        sp.GetRequiredService<IFleetInstanceStore>().Should().NotBeNull();
        sp.GetRequiredService<IInstanceTelemetryCollector>().Should().NotBeNull();
        sp.GetRequiredService<IFleetReportPublisher>().Should().NotBeNull();
        sp.GetRequiredService<IFleetUpdateNotificationSender>().Should().NotBeNull();
    }

    private sealed class FakeSystemConnectionFactory : ISystemConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            throw new System.NotSupportedException("Le smoke-test ne résout que le graphe DI, il n'ouvre pas de connexion.");
    }

    private sealed class FakeTenantQueries : ITenantQueries
    {
        public Task<System.Collections.Generic.IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<TenantDto>>([]);

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantDto?>(null);
    }

    private sealed class FakeEmailTransport : IEmailTransport
    {
        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
