namespace Liakont.Modules.Signature.Tests.Integration;

using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>Partage un unique conteneur PostgreSQL à DEUX bases tenant entre les tests cross-base.</summary>
[CollectionDefinition("SignatureMultiTenantIntegration")]
public sealed class SignatureMultiTenantCollectionFixture : ICollectionFixture<SignatureMultiTenantFixture>
{
}
