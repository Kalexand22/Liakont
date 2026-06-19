namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Fabrique d'un <see cref="ISignatureProvider"/> pour UN type de plug-in (ADR-0027 §4). CHAQUE plug-in
/// (Yousign = SIG07, Wacom = SIG08) fournit son implémentation et s'enregistre dans le conteneur DI ;
/// l'<see cref="ISignatureProviderRegistry"/> les indexe par <see cref="ProviderType"/>. C'est ce qui
/// permet d'ajouter un fournisseur sans aucun <c>if (provider is …)</c> ni flag produit (CLAUDE.md n°6/8/16).
/// Une fabrique DOIT être enregistrée en singleton (capturée par le registre singleton, elle crée des
/// fournisseurs à la demande à partir du <see cref="SignatureProviderAccount"/> du tenant).
/// </summary>
public interface ISignatureProviderFactory
{
    /// <summary>Type de plug-in géré par cette fabrique (clé de registre, insensible à la casse).</summary>
    string ProviderType { get; }

    /// <summary>
    /// Construit un fournisseur pour le compte de signature d'un tenant donné. La résolution des secrets
    /// (chiffrés par tenant) est INTERNE au plug-in — le <paramref name="account"/> n'en porte aucun en clair.
    /// </summary>
    /// <param name="account">Description du compte de signature du tenant (type + configuration non sensible).</param>
    ISignatureProvider Create(SignatureProviderAccount account);
}
