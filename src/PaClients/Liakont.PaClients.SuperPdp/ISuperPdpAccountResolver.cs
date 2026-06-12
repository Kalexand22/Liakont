namespace Liakont.PaClients.SuperPdp;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résout le <see cref="PaAccountDescriptor"/> NON SENSIBLE d'un tenant vers une
/// <see cref="SuperPdpAccountConfig"/> complète — c'est ici que les secrets OAuth (<c>client_id</c> /
/// <c>client_secret</c>) CHIFFRÉS par tenant sont déchiffrés. Le plug-in ne référence QUE
/// <c>Transmission.Contracts</c> (module-rules §6) : il ne peut donc PAS atteindre le coffre du module
/// TenantSettings (<c>ISecretProtector</c>). Cette abstraction est l'unique point d'injection par lequel
/// le Host — qui voit TenantSettings — fournit les secrets déchiffrés au plug-in (« secrets résolus par
/// le plug-in via le coffre », cf. <see cref="PaAccountDescriptor"/>). Le descripteur ne transporte
/// JAMAIS de secret en clair (CLAUDE.md n°10) : la résolution du secret passe TOUJOURS par cette frontière.
/// </summary>
public interface ISuperPdpAccountResolver
{
    /// <summary>
    /// Résout les identifiants (environnement, compte, <c>client_id</c> / <c>client_secret</c> déchiffrés)
    /// du compte Super PDP décrit par <paramref name="account"/>. Lève si le compte est inconnu ou si un
    /// secret est absent (on bloque plutôt que d'envoyer sans authentification — CLAUDE.md n°3).
    /// </summary>
    /// <param name="account">Descripteur non sensible du compte PA du tenant.</param>
    SuperPdpAccountConfig Resolve(PaAccountDescriptor account);
}
