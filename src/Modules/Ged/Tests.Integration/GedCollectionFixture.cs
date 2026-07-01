namespace Liakont.Modules.Ged.Tests.Integration;

using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Collection xUnit partageant un unique conteneur PostgreSQL (<see cref="GedDatabaseFixture"/>) entre les
/// tests d'intégration GED (démarrage du conteneur amorti sur toute la suite ; chaque test crée sa propre base).
/// </summary>
[CollectionDefinition("GedIntegration")]
public sealed class GedCollectionFixture : ICollectionFixture<GedDatabaseFixture>
{
}
