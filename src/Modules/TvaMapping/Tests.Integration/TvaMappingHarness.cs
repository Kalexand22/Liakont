namespace Liakont.Modules.TvaMapping.Tests.Integration;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure.Queries;
using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module (UoW Postgres + requêtes) sur le
/// conteneur de test. Les handlers d'édition (item TVA05) sont construits directement dans les tests
/// avec des doubles de contexte acteur / filtre tenant (voir <c>MappingEditingIntegrationTests</c>),
/// d'où un harness volontairement minimal.
/// </summary>
internal sealed class TvaMappingHarness
{
    public TvaMappingHarness(TvaMappingDatabaseFixture fixture)
    {
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresTvaMappingUnitOfWorkFactory(ConnectionFactory);
        Queries = new PostgresTvaMappingQueries(ConnectionFactory);
    }

    public IConnectionFactory ConnectionFactory { get; }

    public ITvaMappingUnitOfWorkFactory UowFactory { get; }

    public ITvaMappingQueries Queries { get; }
}
