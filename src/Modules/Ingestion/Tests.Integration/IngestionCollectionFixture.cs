namespace Liakont.Modules.Ingestion.Tests.Integration;

using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("IngestionIntegration")]
public sealed class IngestionCollectionFixture : ICollectionFixture<IngestionDatabaseFixture>
{
}
