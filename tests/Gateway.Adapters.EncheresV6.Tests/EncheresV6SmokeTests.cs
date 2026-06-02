using Conformat.Gateway.Adapters.EncheresV6;
using Xunit;

namespace Conformat.Gateway.Adapters.EncheresV6.Tests
{
    /// <summary>Test de fumée du plug-in source EncheresV6 (sera remplacé par les tests réels au lot ADP).</summary>
    public class EncheresV6SmokeTests
    {
        [Fact]
        public void EncheresV6Module_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.Adapters.EncheresV6", EncheresV6Module.Name);
        }
    }
}
