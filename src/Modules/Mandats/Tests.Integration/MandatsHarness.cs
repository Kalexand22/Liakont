namespace Liakont.Modules.Mandats.Tests.Integration;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure.Queries;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module (UoW Postgres + requêtes) sur le conteneur de
/// test. MND01 n'a aucun handler : les tests pilotent directement l'unité de travail avec des
/// <c>company_id</c> explicites (le tenant-scoping est prouvé sur ≥ 2 sociétés).
/// </summary>
internal sealed class MandatsHarness
{
    public MandatsHarness(MandatsDatabaseFixture fixture)
    {
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresMandatsUnitOfWorkFactory(ConnectionFactory);
        Queries = new PostgresMandatsQueries(ConnectionFactory);
        AcceptanceUowFactory = new PostgresSelfBilledAcceptanceUnitOfWorkFactory(ConnectionFactory);
        AcceptanceQueries = new PostgresSelfBilledAcceptanceQueries(ConnectionFactory);
        NumberAllocator = new PostgresSelfBilledNumberAllocator(ConnectionFactory);
    }

    public IConnectionFactory ConnectionFactory { get; }

    public IMandatsUnitOfWorkFactory UowFactory { get; }

    public IMandatsQueries Queries { get; }

    public ISelfBilledAcceptanceUnitOfWorkFactory AcceptanceUowFactory { get; }

    public ISelfBilledAcceptanceQueries AcceptanceQueries { get; }

    public ISelfBilledNumberAllocator NumberAllocator { get; }
}
