using Conformat.Gateway.Service;
using Xunit;

namespace Conformat.Gateway.Service.Tests
{
    /// <summary>Test de fumée de l'hôte (sera remplacé par les tests réels du Service au lot SVC).</summary>
    public class ServiceHostSmokeTests
    {
        [Fact]
        public void ServiceHost_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.Service", ServiceHost.Name);
        }
    }
}
