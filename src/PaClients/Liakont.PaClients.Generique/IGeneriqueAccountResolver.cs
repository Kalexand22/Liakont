namespace Liakont.PaClients.Generique;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résout le <see cref="PaAccountDescriptor"/> NON SENSIBLE d'un tenant vers une
/// <see cref="GeneriqueAccountConfig"/> complète — c'est ici que l'éventuel secret SMTP CHIFFRÉ par
/// tenant est déchiffré (F16 §6.2). Le plug-in ne référence QUE <c>Transmission.Contracts</c>
/// (module-rules §6) : il ne peut donc PAS atteindre le coffre du module TenantSettings
/// (<c>ISecretProtector</c>). Cette abstraction est l'unique point d'injection par lequel le Host — qui
/// voit TenantSettings — fournit la configuration (et les secrets déchiffrés) au plug-in. Le descripteur
/// ne transporte JAMAIS de secret en clair (CLAUDE.md n°10) : la résolution passe TOUJOURS par cette
/// frontière. Patron identique à <c>ISuperPdpAccountResolver</c> / <c>IB2BrouterAccountResolver</c>.
/// </summary>
public interface IGeneriqueAccountResolver
{
    /// <summary>
    /// Résout le canal, la cible et les éventuels identifiants SMTP (déchiffrés) du compte générique
    /// décrit par <paramref name="account"/>. Lève si le compte est mal paramétré (canal/cible absents)
    /// — on bloque plutôt que de livrer faux (CLAUDE.md n°3).
    /// </summary>
    /// <param name="account">Descripteur non sensible du compte PA du tenant.</param>
    GeneriqueAccountConfig Resolve(PaAccountDescriptor account);
}
