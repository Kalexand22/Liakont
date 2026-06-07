namespace Liakont.Console.Api.Tests.Integration;

using Xunit;

/// <summary>
/// Collection partagée : un seul <see cref="ConsoleApiFactory"/> (un conteneur PostgreSQL + un hôte HTTP)
/// pour toute la suite d'intégration de la console. Chaque test lit des données seedées en lecture seule.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleApiCollectionFixture : ICollectionFixture<ConsoleApiFactory>
{
    public const string Name = "ConsoleApi";
}
