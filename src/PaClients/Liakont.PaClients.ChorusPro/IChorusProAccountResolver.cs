namespace Liakont.PaClients.ChorusPro;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résout le <see cref="PaAccountDescriptor"/> NON SENSIBLE d'un tenant vers une
/// <see cref="ChorusProAccountConfig"/> complète — c'est ici que les secrets CHIFFRÉS par tenant
/// (client_id / client_secret PISTE + login / mot de passe du compte technique Chorus Pro) sont
/// déchiffrés, et les URLs verrouillées au raccordement fournies (F18 §2/§3.3). Le plug-in ne référence
/// QUE <c>Transmission.Contracts</c> (module-rules §6) : il ne peut donc PAS atteindre le coffre du
/// module TenantSettings. Cette abstraction est l'unique point d'injection par lequel le Host — qui voit
/// TenantSettings — fournit les secrets déchiffrés au plug-in. Le descripteur ne transporte JAMAIS de
/// secret en clair (CLAUDE.md n°10) : la résolution passe TOUJOURS par cette frontière.
/// </summary>
public interface IChorusProAccountResolver
{
    /// <summary>
    /// Résout les identifiants (environnement, URLs, creds PISTE + compte technique déchiffrés) du compte
    /// Chorus Pro décrit par <paramref name="account"/>. Lève si le compte est inconnu ou si une valeur
    /// obligatoire manque (on bloque plutôt que d'envoyer sans authentification — CLAUDE.md n°3).
    /// </summary>
    /// <param name="account">Descripteur non sensible du compte PA du tenant.</param>
    ChorusProAccountConfig Resolve(PaAccountDescriptor account);
}
