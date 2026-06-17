namespace Liakont.SignatureProviders.Yousign;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Résout le <see cref="SignatureProviderAccount"/> NON SENSIBLE d'un tenant vers une
/// <see cref="YousignAccountConfig"/> complète — c'est ici que la clé API et le secret webhook CHIFFRÉS par
/// tenant sont DÉCHIFFRÉS (ADR-0029 §6). Le plug-in ne référence QUE <c>Signature.Contracts</c>
/// (module-rules §6 ; INV-YOUSIGN-2) : il ne peut donc PAS atteindre le coffre du tenant
/// (<c>ISecretProtector</c> du module TenantSettings). Cette abstraction est l'UNIQUE point d'injection par
/// lequel le Host — qui voit le coffre — fournit les secrets déchiffrés au plug-in, EN MÉMOIRE uniquement.
/// </summary>
public interface IYousignAccountResolver
{
    /// <summary>
    /// Résout l'environnement + les secrets (clé API + secret webhook déchiffrés) du compte Yousign décrit
    /// par <paramref name="account"/>. Lève si le compte est inconnu ou si un secret est absent (on bloque
    /// plutôt que d'appeler sans authentification — CLAUDE.md n°3).
    /// </summary>
    /// <param name="account">Descripteur non sensible du compte de signature du tenant.</param>
    YousignAccountConfig Resolve(SignatureProviderAccount account);
}
