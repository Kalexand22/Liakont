namespace Liakont.PaClients.Generique.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;

/// <summary>Résolveur de compte FACTICE : rend une configuration fixe (canal + cible + secret optionnel).</summary>
internal sealed class StubAccountResolver(GeneriqueAccountConfig config) : IGeneriqueAccountResolver
{
    public GeneriqueAccountConfig Resolve(PaAccountDescriptor account) => config;
}
