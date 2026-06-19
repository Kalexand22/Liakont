namespace Liakont.Modules.Mandats.Tests.Integration;

using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Xunit;

/// <summary>Collection xUnit partageant un unique conteneur PostgreSQL entre les classes de tests d'intégration.</summary>
[CollectionDefinition("MandatsIntegration")]
public sealed class MandatsCollectionFixture : ICollectionFixture<MandatsDatabaseFixture>
{
}
