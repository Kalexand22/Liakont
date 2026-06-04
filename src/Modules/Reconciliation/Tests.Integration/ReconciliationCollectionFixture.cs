namespace Liakont.Modules.Reconciliation.Tests.Integration;

using Liakont.Modules.Reconciliation.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("ReconciliationIntegration")]
public sealed class ReconciliationCollectionFixture : ICollectionFixture<ReconciliationDatabaseFixture>
{
}
