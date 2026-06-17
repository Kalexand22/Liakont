namespace Liakont.Modules.Signature.Tests.Integration;

using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Collection xUnit partageant le conteneur PostgreSQL multi-tenant entre les tests d'intégration du module
/// Signature (un seul démarrage de conteneur — coût Testcontainers amorti).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SignatureCollectionFixture : ICollectionFixture<SignatureMultiTenantFixture>
{
    public const string Name = "SignatureIntegration";
}
