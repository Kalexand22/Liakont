namespace Liakont.Modules.TenantSettings.Tests.Integration;

using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("TenantSettingsIntegration")]
public sealed class TenantSettingsCollectionFixture : ICollectionFixture<TenantSettingsDatabaseFixture>
{
}
