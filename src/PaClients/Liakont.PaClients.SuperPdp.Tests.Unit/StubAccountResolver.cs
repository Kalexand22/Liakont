namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résolveur de compte de test : retourne une <see cref="SuperPdpAccountConfig"/> fixe (identifiants
/// OAuth FICTIFS — aucune donnée client, CLAUDE.md n°7). Tient lieu, en test, de l'adaptateur que le Host
/// branche en production (déchiffrement des secrets du tenant).
/// </summary>
internal sealed class StubAccountResolver : ISuperPdpAccountResolver
{
    private readonly SuperPdpAccountConfig _config;

    public StubAccountResolver(SuperPdpAccountConfig config) => _config = config;

    public SuperPdpAccountConfig Resolve(PaAccountDescriptor account) => _config;
}
