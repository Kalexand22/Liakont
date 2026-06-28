namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Lookup de suspension (OPS03.4 lot B) : profil absent = ACTIF (jamais de suspension implicite),
/// « Suspendu » = suspendu, cache court (une seule lecture pour deux appels), invalidation
/// explicite, et FAIL-OPEN sur erreur de lecture (une panne de base ne coupe pas les tenants).
/// </summary>
public sealed class TenantSuspensionLookupTests
{
    private readonly CountingScopeFactory _scopeFactory = new();

    [Fact]
    public async Task A_Tenant_Without_Profile_Is_Active()
    {
        _scopeFactory.Statut = null;

        (await CreateSut().IsSuspendedAsync("acme")).Should().BeFalse();
    }

    [Fact]
    public async Task A_Suspended_Profile_Is_Suspended()
    {
        _scopeFactory.Statut = "Suspendu";

        (await CreateSut().IsSuspendedAsync("acme")).Should().BeTrue();
    }

    [Fact]
    public async Task An_Active_Profile_Is_Active()
    {
        _scopeFactory.Statut = "Actif";

        (await CreateSut().IsSuspendedAsync("acme")).Should().BeFalse();
    }

    [Fact]
    public async Task The_Status_Is_Cached_Between_Calls()
    {
        _scopeFactory.Statut = "Suspendu";
        var sut = CreateSut();

        await sut.IsSuspendedAsync("acme");
        await sut.IsSuspendedAsync("acme");

        _scopeFactory.Reads.Should().Be(1, "le statut est mis en cache (TTL court) — pas une lecture par requête");
    }

    [Fact]
    public async Task Invalidate_Forces_The_Next_Read()
    {
        _scopeFactory.Statut = "Actif";
        var sut = CreateSut();
        (await sut.IsSuspendedAsync("acme")).Should().BeFalse();

        _scopeFactory.Statut = "Suspendu";
        sut.Invalidate("acme");

        (await sut.IsSuspendedAsync("acme")).Should().BeTrue("l'invalidation rend la suspension immédiate depuis la console");
    }

    [Fact]
    public async Task A_Read_Failure_Is_Fail_Open_And_Not_Cached()
    {
        _scopeFactory.ThrowOnRead = new InvalidOperationException("base indisponible");
        var sut = CreateSut();

        (await sut.IsSuspendedAsync("acme")).Should().BeFalse("fail-open : une panne de lecture ne coupe jamais le tenant");

        // La panne n'est PAS mise en cache : la lecture suivante retente (et applique le vrai statut).
        _scopeFactory.ThrowOnRead = null;
        _scopeFactory.Statut = "Suspendu";
        (await sut.IsSuspendedAsync("acme")).Should().BeTrue();
    }

    [Fact]
    public async Task A_Blank_TenantId_Is_Active_Without_Any_Read()
    {
        (await CreateSut().IsSuspendedAsync(string.Empty)).Should().BeFalse();
        _scopeFactory.Reads.Should().Be(0);
    }

    private TenantSuspensionLookup CreateSut() =>
        new(_scopeFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<TenantSuspensionLookup>.Instance);

    private sealed class CountingScopeFactory : ITenantScopeFactory
    {
        public string? Statut { get; set; }

        public Exception? ThrowOnRead { get; set; }

        public int Reads { get; private set; }

        public ITenantScope Create(string tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantSettingsQueries>(new StatutQueries(this));
            return new FakeScope(tenantId, services.BuildServiceProvider());
        }

        private sealed class StatutQueries : ITenantSettingsQueries
        {
            private readonly CountingScopeFactory _owner;

            public StatutQueries(CountingScopeFactory owner) => _owner = owner;

            public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default)
            {
                _owner.Reads++;
                if (_owner.ThrowOnRead is not null)
                {
                    throw _owner.ThrowOnRead;
                }

                return Task.FromResult(_owner.Statut);
            }

            public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private sealed class FakeScope : ITenantScope
        {
            private readonly ServiceProvider _provider;

            public FakeScope(string tenantId, ServiceProvider provider)
            {
                TenantId = tenantId;
                _provider = provider;
            }

            public string TenantId { get; }

            public IServiceProvider Services => _provider;

            public ValueTask DisposeAsync() => _provider.DisposeAsync();
        }
    }
}
