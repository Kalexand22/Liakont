namespace Liakont.Modules.Payments.Tests.Integration;

using Liakont.Modules.Payments.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("PaymentsIntegration")]
public sealed class PaymentsCollectionFixture : ICollectionFixture<PaymentsDatabaseFixture>
{
}
