namespace Stratum.Modules.Identity.Tests.Integration;

using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("Identity")]
public sealed class IdentityCollectionFixture : ICollectionFixture<IdentityDatabaseFixture>
{
}
