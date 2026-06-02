using Conformat.Gateway.PaClients.SuperPdp;
using Xunit;

namespace Conformat.Gateway.PaClients.SuperPdp.Tests
{
    /// <summary>Test de fumée du plug-in PA Super PDP (sera remplacé par les tests réels au lot PAS).</summary>
    public class SuperPdpSmokeTests
    {
        [Fact]
        public void SuperPdpModule_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.PaClients.SuperPdp", SuperPdpModule.Name);
        }
    }
}
