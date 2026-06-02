using Conformat.Gateway.PaClients.B2Brouter;
using Xunit;

namespace Conformat.Gateway.PaClients.B2Brouter.Tests
{
    /// <summary>Test de fumée du plug-in PA B2Brouter (sera remplacé par les tests réels au lot PAB).</summary>
    public class B2BrouterSmokeTests
    {
        [Fact]
        public void B2BrouterModule_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.PaClients.B2Brouter", B2BrouterModule.Name);
        }
    }
}
