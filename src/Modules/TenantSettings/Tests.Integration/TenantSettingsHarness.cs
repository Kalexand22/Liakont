namespace Liakont.Modules.TenantSettings.Tests.Integration;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure.Queries;
using Liakont.Modules.TenantSettings.Tests.Integration.Doubles;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Assemble les vraies dépendances du module (UoW Postgres, requêtes, chiffrement Data Protection,
/// journal capturant) pour un (company_id, user_id) donné — isolation par test.
/// </summary>
internal sealed class TenantSettingsHarness
{
    public TenantSettingsHarness(TenantSettingsDatabaseFixture fixture, Guid companyId, Guid userId)
    {
        CompanyId = companyId;
        UserId = userId;
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresTenantSettingsUnitOfWorkFactory(ConnectionFactory);
        Queries = new PostgresTenantSettingsQueries(ConnectionFactory);
        SecretProtector = fixture.CreateSecretProtector();
        CompanyFilter = new TestCompanyFilter(companyId);
        ActorAccessor = new TestActorContextAccessor(userId, companyId);
        ActivityLogger = new CapturingActivityLogger();
        Journal = new TenantSettingsJournal(ActivityLogger, ActorAccessor);
    }

    public Guid CompanyId { get; }

    public Guid UserId { get; }

    public IConnectionFactory ConnectionFactory { get; }

    public ITenantSettingsUnitOfWorkFactory UowFactory { get; }

    public ITenantSettingsQueries Queries { get; }

    public ISecretProtector SecretProtector { get; }

    public ICompanyFilter CompanyFilter { get; }

    public TestActorContextAccessor ActorAccessor { get; }

    public CapturingActivityLogger ActivityLogger { get; }

    public TenantSettingsJournal Journal { get; }
}
