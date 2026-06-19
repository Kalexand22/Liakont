namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique d'un <see cref="IPaClient"/> pour UN type de plug-in PA. CHAQUE plug-in (PAA02 Fake,
/// PAB B2Brouter, PAS Super PDP) fournit son implémentation et s'enregistre dans le conteneur DI ;
/// le <see cref="IPaClientRegistry"/> les indexe par <see cref="PaType"/>. C'est ce qui permet au
/// produit d'ajouter une PA sans aucun <c>if (pa is …)</c> ni flag produit (CLAUDE.md n°6/8/16).
/// Une fabrique DOIT être enregistrée en singleton (elle est capturée par le registre singleton et
/// crée des clients à la demande à partir du <see cref="PaAccountDescriptor"/> du tenant).
/// </summary>
public interface IPaClientFactory
{
    /// <summary>Type de plug-in géré par cette fabrique (clé de registre, insensible à la casse).</summary>
    string PaType { get; }

    /// <summary>
    /// Mode d'authentification du type de plug-in — capacité STATIQUE lue par la console (champs de creds
    /// à présenter) sans instancier de client. Par défaut <see cref="PaAuthMode.ApiKey"/> ; un plug-in
    /// OAuth2 (Super PDP) surcharge ce membre. Générique, jamais un <c>if (pa is …)</c> (CLAUDE.md n°8/16).
    /// </summary>
    PaAuthMode AuthMode => PaAuthMode.ApiKey;

    /// <summary>
    /// Construit un client pour le compte PA d'un tenant donné. La résolution des secrets (chiffrés
    /// par tenant) est interne au plug-in — le <paramref name="account"/> n'en porte aucun en clair.
    /// </summary>
    /// <param name="account">Description du compte PA du tenant (type + configuration non sensible).</param>
    IPaClient Create(PaAccountDescriptor account);
}
