namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résolveur de compte de test : retourne une <see cref="B2BrouterAccountConfig"/> fixe (clé API
/// FICTIVE — aucune donnée client, CLAUDE.md n°7). Tient lieu, en test, de l'adaptateur que le Host
/// branche en production (déchiffrement de la clé du tenant).
/// </summary>
internal sealed class StubAccountResolver : IB2BrouterAccountResolver
{
    private readonly B2BrouterAccountConfig _config;

    public StubAccountResolver(B2BrouterAccountConfig config) => _config = config;

    public B2BrouterAccountConfig Resolve(PaAccountDescriptor account) => _config;
}
