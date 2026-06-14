namespace Liakont.Modules.TenantSettings.Tests.Unit;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;
using Xunit;

/// <summary>
/// Garde anti-injection des écritures à société explicite (OPS03 lot C — partagée seed/profil) :
/// l'override est honoré sans acteur de tenant, avec un acteur de la MÊME société, ou sur un tenant
/// SANS profil (provisioning console : l'opérateur porte le company_id de SON tenant) ; dès qu'un
/// profil existe, un explicite qui contredit l'acteur est REFUSÉ — la protection cross-tenant reste
/// entière hors du strict état create-only.
/// </summary>
public sealed class TenantSettingsCompanyOverrideGuardTests
{
    private static readonly Guid TargetCompanyId = Guid.Parse("11111111-1111-4111-a111-111111111111");
    private static readonly Guid OperatorCompanyId = Guid.Parse("22222222-2222-4222-a222-222222222222");

    [Fact]
    public async Task Without_Explicit_CompanyId_The_Ambient_Company_Is_Used()
    {
        var resolved = await TenantSettingsCompanyOverrideGuard.ResolveAsync(
            explicitCompanyId: null,
            new FakeCompanyFilter(OperatorCompanyId),
            Actor(OperatorCompanyId),
            Queries(currentCompanyId: OperatorCompanyId),
            CancellationToken.None);

        Assert.Equal(OperatorCompanyId, resolved);
    }

    [Fact]
    public async Task An_Explicit_CompanyId_Without_Tenant_Actor_Is_Honored()
    {
        // Amorçage / endpoint d'administration sans claim company : chemin privilégié historique.
        var resolved = await TenantSettingsCompanyOverrideGuard.ResolveAsync(
            TargetCompanyId,
            new FakeCompanyFilter(null),
            Actor(actorCompanyId: null),
            Queries(currentCompanyId: null),
            CancellationToken.None);

        Assert.Equal(TargetCompanyId, resolved);
    }

    [Fact]
    public async Task A_Foreign_Actor_Is_Honored_On_A_Tenant_WITHOUT_Profile()
    {
        // Provisioning console (OPS03) : l'opérateur porte le company_id de SON tenant ; le tenant
        // cible n'a AUCUN profil (create-only — rien à corrompre) → l'explicite fait foi.
        var resolved = await TenantSettingsCompanyOverrideGuard.ResolveAsync(
            TargetCompanyId,
            new FakeCompanyFilter(OperatorCompanyId),
            Actor(OperatorCompanyId),
            Queries(currentCompanyId: null),
            CancellationToken.None);

        Assert.Equal(TargetCompanyId, resolved);
    }

    [Fact]
    public async Task A_Foreign_Actor_Is_REFUSED_On_A_Tenant_WITH_Profile()
    {
        // Dès qu'un profil existe, la garde anti-injection cross-tenant reste entière.
        await Assert.ThrowsAsync<ConflictException>(() =>
            TenantSettingsCompanyOverrideGuard.ResolveAsync(
                TargetCompanyId,
                new FakeCompanyFilter(OperatorCompanyId),
                Actor(OperatorCompanyId),
                Queries(currentCompanyId: TargetCompanyId),
                CancellationToken.None));
    }

    [Fact]
    public async Task An_Actor_Of_The_Same_Company_Is_Honored_Without_Profile_Check()
    {
        var resolved = await TenantSettingsCompanyOverrideGuard.ResolveAsync(
            TargetCompanyId,
            new FakeCompanyFilter(TargetCompanyId),
            Actor(TargetCompanyId),
            Queries(currentCompanyId: TargetCompanyId),
            CancellationToken.None);

        Assert.Equal(TargetCompanyId, resolved);
    }

    private static FakeActorContextAccessor Actor(Guid? actorCompanyId) => new(actorCompanyId);

    private static FakeSettingsQueries Queries(Guid? currentCompanyId) => new(currentCompanyId);

    private sealed class FakeCompanyFilter : ICompanyFilter
    {
        private readonly Guid? _companyId;

        public FakeCompanyFilter(Guid? companyId) => _companyId = companyId;

        public Guid GetRequiredCompanyId() =>
            _companyId ?? throw new InvalidOperationException("No company in context.");
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId) => Current = new Ctx(companyId);

        public IActorContext Current { get; }

        private sealed class Ctx : IActorContext
        {
            public Ctx(Guid? companyId) => CompanyId = companyId;

            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => CompanyId is not null;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }

    private sealed class FakeSettingsQueries : ITenantSettingsQueries
    {
        private readonly Guid? _currentCompanyId;

        public FakeSettingsQueries(Guid? currentCompanyId) => _currentCompanyId = currentCompanyId;

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_currentCompanyId);

        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
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
}
