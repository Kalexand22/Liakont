namespace Liakont.Tests.E2E;

using System.Diagnostics.CodeAnalysis;
using Xunit;

/// <summary>
/// Collection de tests partagée pour les tests E2E Keycloak (OIDC). Câble
/// <see cref="KeycloakE2EWebFactory"/> (application + Keycloak + PostgreSQL)
/// et <see cref="PlaywrightFixture"/> (navigateur).
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "La convention xUnit impose le suffixe 'Collection' pour les définitions de collection.")]
public sealed class KeycloakE2ECollection
    : ICollectionFixture<KeycloakE2EWebFactory>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "KeycloakE2E";
}
