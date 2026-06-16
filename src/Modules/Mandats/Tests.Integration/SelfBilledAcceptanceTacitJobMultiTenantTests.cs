namespace Liakont.Modules.Mandats.Tests.Integration;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure.Queries;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Jobs;
using Xunit;

/// <summary>
/// Bascule tacite (MND04, ADR-0024 §4) au travers du VRAI <c>TenantJobRunner</c> (SOL06) sur DEUX bases
/// tenant réelles (Testcontainers) : le runner bascule la connexion par tenant, chaque tenant ne traite QUE
/// ses propres acceptations dues (isolation cross-base, INV-MANDATS-1/INV-ACCEPT-6), et la bascule n'a lieu
/// que pour les acceptations en attente à échéance échue (mandat écrit + délai). Prouve « isolation/scoping
/// cross-tenant ≥ 2 bases » de l'acceptance MND04.
/// </summary>
public sealed class SelfBilledAcceptanceTacitJobMultiTenantTests : IAsyncLifetime
{
    // Échéance clairement échue (le service utilise TimeProvider.System dans le scope) et entrée en attente antérieure.
    private static readonly DateTimeOffset PendingSince = new(1999, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ElapsedDeadline = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly MandatsMultiTenantFixture _fixture = new();
    private ITenantConnectionFactory _tenantConnectionFactory = null!;
    private ITenantScopeFactory _scopeFactory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _tenantConnectionFactory = _fixture.CreateTenantConnectionFactory();
        _scopeFactory = new TestTenantScopeFactory(_tenantConnectionFactory);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RunForAllTenants_Switches_Due_Acceptances_Per_Tenant_And_Keeps_Bases_Isolated()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var dueA = Guid.NewGuid();
        var nullDeadlineA = Guid.NewGuid();
        var dueB = Guid.NewGuid();

        // Tenant A : une acceptation due + une sans échéance (jamais tacite). Tenant B : une acceptation due.
        await SeedPendingAsync(MandatsMultiTenantFixture.TenantA, companyA, dueA, ElapsedDeadline);
        await SeedPendingAsync(MandatsMultiTenantFixture.TenantA, companyA, nullDeadlineA, deadline: null);
        await SeedPendingAsync(MandatsMultiTenantFixture.TenantB, companyB, dueB, ElapsedDeadline);

        var runner = new TenantJobRunner(
            _fixture.CreateTenantQueries(), _scopeFactory, NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(new SelfBilledAcceptanceTacitJob());

        summary.TotalTenants.Should().Be(2);
        summary.SucceededCount.Should().Be(2);
        summary.FailedCount.Should().Be(0);

        // Tenant A : la due bascule, la sans-échéance reste en attente.
        (await StateAsync(MandatsMultiTenantFixture.TenantA, companyA, dueA))
            .Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted));
        (await StateAsync(MandatsMultiTenantFixture.TenantA, companyA, nullDeadlineA))
            .Should().Be(nameof(SelfBilledAcceptanceState.PendingAcceptance));

        // Tenant B : sa propre due bascule (connexion basculée vers sa base).
        (await StateAsync(MandatsMultiTenantFixture.TenantB, companyB, dueB))
            .Should().Be(nameof(SelfBilledAcceptanceState.TacitlyAccepted));

        // Isolation cross-base : aucune acceptation d'un tenant n'existe dans la base de l'autre.
        (await StateAsync(MandatsMultiTenantFixture.TenantB, companyA, dueA))
            .Should().BeNull("l'acceptation du tenant A n'existe pas dans la base du tenant B (CLAUDE.md n°9).");
        (await StateAsync(MandatsMultiTenantFixture.TenantA, companyB, dueB))
            .Should().BeNull("l'acceptation du tenant B n'existe pas dans la base du tenant A.");
    }

    private async Task SeedPendingAsync(string tenantId, Guid companyId, Guid documentId, DateTimeOffset? deadline)
    {
        var factory = _fixture.CreateConnectionFactory(tenantId);
        var uowFactory = new PostgresSelfBilledAcceptanceUnitOfWorkFactory(factory);

        var acceptance = SelfBilledAcceptance.Create(companyId, documentId, PendingSince, deadline);
        await using var uow = await uowFactory.BeginAsync();
        var entry = SelfBilledAcceptanceLogFactory.ForCreation(acceptance, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(acceptance, entry);
        await uow.CommitAsync();
    }

    private async Task<string?> StateAsync(string tenantId, Guid companyId, Guid documentId)
    {
        var queries = new PostgresSelfBilledAcceptanceQueries(_fixture.CreateConnectionFactory(tenantId));
        var dto = await queries.GetAcceptance(companyId, documentId);
        return dto?.State;
    }

    /// <summary>
    /// Double du seam que le Host fournit en production : construit un scope dont l'<see cref="IConnectionFactory"/>
    /// est routé vers le tenant demandé (via le vrai <see cref="TenantScopedConnectionFactory"/>) ET où le module
    /// Mandats est enregistré (DI de production) — le job y résout <c>ITacitAcceptanceService</c>.
    /// </summary>
    private sealed class TestTenantScopeFactory : ITenantScopeFactory
    {
        private readonly ITenantConnectionFactory _tenantConnectionFactory;

        public TestTenantScopeFactory(ITenantConnectionFactory tenantConnectionFactory)
            => _tenantConnectionFactory = tenantConnectionFactory;

        public ITenantScope Create(string tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_tenantConnectionFactory);
            services.AddSingleton<ITenantContext>(new FixedTenantContext(tenantId));
            services.AddScoped<IConnectionFactory, TenantScopedConnectionFactory>();
            services.AddMandatsModule();
            var provider = services.BuildServiceProvider();
            return new TestTenantScope(provider, provider.CreateAsyncScope(), tenantId);
        }
    }

    private sealed class TestTenantScope : ITenantScope
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public TestTenantScope(ServiceProvider provider, AsyncServiceScope scope, string tenantId)
        {
            _provider = provider;
            _scope = scope;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IServiceProvider Services => _scope.ServiceProvider;

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(string tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }
}
