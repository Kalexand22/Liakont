namespace Liakont.Modules.TvaMapping.Tests.Integration;

using Liakont.Modules.TvaMapping.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("TvaMappingIntegration")]
public sealed class TvaMappingCollectionFixture : ICollectionFixture<TvaMappingDatabaseFixture>
{
}
