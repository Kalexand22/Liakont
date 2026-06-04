namespace Liakont.Modules.Payments.Tests.Integration;

using Liakont.Modules.Payments.Application;
using Liakont.Modules.Payments.Contracts.Queries;
using Liakont.Modules.Payments.Infrastructure;
using Liakont.Modules.Payments.Infrastructure.Queries;
using Liakont.Modules.Payments.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module (UoW Postgres + requêtes) sur le conteneur de
/// test. Harness volontairement minimal.
/// </summary>
internal sealed class PaymentsHarness
{
    public PaymentsHarness(PaymentsDatabaseFixture fixture)
    {
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresPaymentUnitOfWorkFactory(ConnectionFactory);
        Queries = new PostgresPaymentQueries(ConnectionFactory);
    }

    public IConnectionFactory ConnectionFactory { get; }

    public IPaymentUnitOfWorkFactory UowFactory { get; }

    public IPaymentQueries Queries { get; }
}
