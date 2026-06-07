namespace Liakont.Modules.Supervision.Tests.Integration;

using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("SupervisionIntegration")]
public sealed class SupervisionCollectionFixture : ICollectionFixture<SupervisionDatabaseFixture>
{
}
