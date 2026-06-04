namespace Liakont.Modules.Documents.Tests.Integration;

using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Liakont.Modules.Documents.Tests.Integration.Fixtures;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances de persistance du module (UoW Postgres + requêtes) sur le conteneur
/// de test. Harness volontairement minimal — les chemins spécifiques (port d'ingestion) sont câblés
/// directement dans les tests.
/// </summary>
internal sealed class DocumentsHarness
{
    public DocumentsHarness(DocumentsDatabaseFixture fixture)
    {
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresDocumentUnitOfWorkFactory(ConnectionFactory);
        Queries = new PostgresDocumentQueries(ConnectionFactory);
    }

    public IConnectionFactory ConnectionFactory { get; }

    public IDocumentUnitOfWorkFactory UowFactory { get; }

    public IDocumentQueries Queries { get; }
}
