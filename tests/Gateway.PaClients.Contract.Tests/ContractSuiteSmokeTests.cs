using Conformat.Gateway.Core;
using Xunit;

namespace Conformat.Gateway.PaClients.Contract.Tests
{
    /// <summary>
    /// Test de fumée : confirme que la suite de contrat référence bien le Core (futur foyer
    /// d'IPaClient / PaCapabilities) et que la chaîne de tests s'exécute. La suite de contrat
    /// réelle, exécutée contre chaque implémentation de PA, arrive au lot PAA.
    /// </summary>
    public class ContractSuiteSmokeTests
    {
        [Fact]
        public void Core_assembly_is_referenceable()
        {
            Assert.Equal("Gateway.Core", CoreModule.Name);
        }
    }
}
