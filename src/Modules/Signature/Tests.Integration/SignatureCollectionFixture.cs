namespace Liakont.Modules.Signature.Tests.Integration;

using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>Partage un unique conteneur PostgreSQL (mono-tenant) entre les tests de la collection.</summary>
[CollectionDefinition("SignatureIntegration")]
public sealed class SignatureCollectionFixture : ICollectionFixture<SignatureDatabaseFixture>
{
}
