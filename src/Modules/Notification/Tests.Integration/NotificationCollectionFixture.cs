namespace Stratum.Modules.Notification.Tests.Integration;

using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("NotificationIntegration")]
public sealed class NotificationCollectionFixture : ICollectionFixture<NotificationDatabaseFixture>
{
}
