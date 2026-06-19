namespace Liakont.Modules.Signature.Application;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Persistance tenant-scopée des comptes de signature d'un tenant (ADR-0029 §6 ; patron <c>PaAccount</c>).
/// Les secrets (clé API + secret webhook) sont stockés CHIFFRÉS par tenant (jamais en clair — CLAUDE.md n°10)
/// et exposés via le <see cref="SignatureProviderAccount"/> sous forme de texte chiffré dans ses
/// <c>Settings</c> ; le déchiffrement est INTERNE au plug-in (résolveur du Host). Toute opération est
/// scopée par <c>company_id</c> (CLAUDE.md n°9).
/// </summary>
public interface ISignatureAccountStore
{
    /// <summary>
    /// Charge le descripteur du compte de signature ACTIF d'un tenant pour un type de fournisseur, ou
    /// <c>null</c> s'il n'en existe pas. Les <c>Settings</c> portent l'environnement + les secrets CHIFFRÉS.
    /// </summary>
    /// <param name="companyId">Tenant (clé <c>company_id</c>).</param>
    /// <param name="providerType">Type de fournisseur (insensible à la casse).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureProviderAccount?> GetActiveAccountAsync(
        Guid companyId, string providerType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée ou met à jour le compte de signature d'un tenant (clé <c>(company_id, provider_type)</c>). Les
    /// valeurs de secret reçues sont DÉJÀ chiffrées (le store ne chiffre pas et ne voit jamais le clair).
    /// </summary>
    /// <param name="companyId">Tenant (clé <c>company_id</c>).</param>
    /// <param name="providerType">Type de fournisseur.</param>
    /// <param name="environment">Environnement déclaré (« Sandbox » / « Production »).</param>
    /// <param name="accountIdentifiers">Identifiants non secrets (JSON opaque).</param>
    /// <param name="encryptedApiKey">Clé API chiffrée (texte opaque).</param>
    /// <param name="encryptedWebhookSecret">Secret webhook chiffré (texte opaque).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task UpsertAsync(
        Guid companyId,
        string providerType,
        string environment,
        string accountIdentifiers,
        string encryptedApiKey,
        string encryptedWebhookSecret,
        CancellationToken cancellationToken = default);
}
