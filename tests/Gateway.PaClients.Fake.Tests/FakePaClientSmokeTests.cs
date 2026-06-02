using Conformat.Gateway.PaClients.Fake;
using Xunit;

namespace Conformat.Gateway.PaClients.Fake.Tests
{
    /// <summary>Test de fumée du plug-in PA factice (sera remplacé par les tests réels au lot PAA).</summary>
    public class FakePaClientSmokeTests
    {
        [Fact]
        public void FakePaClientModule_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.PaClients.Fake", FakePaClientModule.Name);
        }
    }
}
