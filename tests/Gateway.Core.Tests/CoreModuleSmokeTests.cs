using Conformat.Gateway.Core;
using Xunit;

namespace Conformat.Gateway.Core.Tests
{
    /// <summary>
    /// Test de fumée du socle : confirme que l'assembly Gateway.Core se charge, que la frontière
    /// de référence Tests → Core fonctionne, et que la chaîne de tests (xUnit + net48) s'exécute.
    /// Sera remplacé par les tests métier réels du lot PIV.
    /// </summary>
    public class CoreModuleSmokeTests
    {
        [Fact]
        public void CoreModule_exposes_its_logical_name()
        {
            Assert.Equal("Gateway.Core", CoreModule.Name);
        }
    }
}
