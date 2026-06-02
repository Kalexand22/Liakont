using Conformat.Gateway.App;
using Xunit;

namespace Conformat.Gateway.App.Tests
{
    /// <summary>Test de fumée de la console WPF (sera remplacé par les tests de ViewModels au lot WPF).</summary>
    public class AppModuleSmokeTests
    {
        [Fact]
        public void AppModule_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.App", AppModule.Name);
        }
    }
}
