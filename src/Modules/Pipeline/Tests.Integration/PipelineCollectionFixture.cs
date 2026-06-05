namespace Liakont.Modules.Pipeline.Tests.Integration;

using Liakont.Modules.Pipeline.Tests.Integration.Fixtures;
using Xunit;

/// <summary>Partage UN conteneur PostgreSQL pour toute la collection d'intégration du module Pipeline.</summary>
[CollectionDefinition("PipelineIntegration")]
public sealed class PipelineCollectionFixture : ICollectionFixture<PipelineDatabaseFixture>
{
}
