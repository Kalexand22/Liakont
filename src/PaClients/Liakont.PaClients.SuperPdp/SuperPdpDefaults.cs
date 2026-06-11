namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Constantes du plug-in Super PDP. Chaque valeur porte son STATUT de vérification (F14 §2/§12) —
/// jamais une valeur inventée (CLAUDE.md n°2/15). Centralisées ici pour qu'un changement d'API (URL,
/// préfixe de version, chemin) tienne en un seul point, et pour que la levée d'un point ouvert Oₙ
/// (PAS03, sandbox réelle) ne touche que CE fichier.
/// <list type="bullet">
///   <item>✅ <b>confirmés</b> : préfixe de version <c>v1.beta</c> (public), token-endpoint
///   <c>oauth2/token</c> et base <c>api.superpdp.tech</c> (test OAuth réel du 2026-06-11, en-tête de
///   <c>orchestration/items/PAS.yaml</c>).</item>
///   <item>🟠 <b>cible de conception</b> (chemins métier exacts) : à confirmer contre l'OpenAPI en
///   sandbox (PAS03, F14 §12 O2). Le plug-in déclare ses capacités HONNÊTEMENT (B2C seul vérifié — §5) :
///   tant qu'un chemin n'est pas confirmé, sa capacité reste <c>false</c> et l'appel dégrade en résultat
///   typé ou lève une exception traçable, jamais un faux envoi.</item>
/// </list>
/// </summary>
public static class SuperPdpDefaults
{
    /// <summary>Clé de registre du plug-in (résolution par <see cref="Modules.Transmission.Contracts.PaAccountDescriptor.PaType"/>, insensible à la casse).</summary>
    public const string PaTypeKey = "SuperPdp";

    /// <summary>Nom affichable de la PA, porté dans les messages opérateur français (CLAUDE.md n°12).</summary>
    public const string PaName = "Super PDP";

    /// <summary>Nom du client HTTP nommé enregistré via <c>AddHttpClient</c> (F14 §7).</summary>
    public const string HttpClientName = "Liakont.PaClients.SuperPdp";

    /// <summary>
    /// Préfixe de version des endpoints métier (✅ confirmé public : <c>GET /v1.beta/companies/me</c> —
    /// F14 §2, repo pimeo/superpdp-nodejs-oauth-example). Sans barre de début/fin : combiné aux chemins
    /// relatifs ci-dessous.
    /// </summary>
    public const string ApiVersionPrefix = "v1.beta";

    /// <summary>
    /// Chemin du token-endpoint OAuth 2.0 (✅ confirmé : <c>POST &lt;base&gt;/oauth2/token</c>,
    /// <c>grant_type=client_credentials</c> → bearer, test réel du 2026-06-11). HORS préfixe de version.
    /// </summary>
    public const string TokenPath = "oauth2/token";

    /// <summary>
    /// Chemin relatif d'émission de document (🟠 cible de conception F14 §3.2 : <c>POST /v1.beta/invoices</c>
    /// — ou <c>/documents</c> ; à confirmer OpenAPI sandbox PAS03, O2). Relatif au préfixe de version.
    /// </summary>
    public const string InvoicesPath = "invoices";

    /// <summary>
    /// Base d'API SANDBOX (✅ confirmée par le test OAuth réel du 2026-06-11 : <c>api.superpdp.tech</c>).
    /// <para>
    /// ⛔ La base PRODUCTION n'est PAS confirmée (F14 §12 O1 — Super PDP peut exposer un hôte prod distinct
    /// ou utiliser le même hôte avec des comptes séparés : non tranché). On n'invente PAS un hôte fictif
    /// (CLAUDE.md n°15) : un compte configuré en <c>Production</c> est BLOQUÉ à la construction
    /// (<see cref="SuperPdpAccountConfig.BaseUrl"/> lève <see cref="NotSupportedException"/>) jusqu'à ce que
    /// PAS03 confirme la base de production — bloquer plutôt qu'envoyer faux (CLAUDE.md n°3).
    /// </para>
    /// </summary>
    public const string SandboxBaseUrl = "https://api.superpdp.tech";

    /// <summary>
    /// Marge de sécurité retranchée à <c>expires_in</c> avant de considérer le jeton expiré : le jeton est
    /// renouvelé un peu AVANT son échéance réelle pour éviter d'envoyer une requête avec un jeton expirant
    /// en vol (F14 §3.1 : « met en cache le jeton et le renouvelle avant expiration »).
    /// </summary>
    public static readonly TimeSpan TokenExpirySkew = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Délai d'attente HTTP par appel : 60 s (l'API peut être lente à la création + envoi — F14 §7, même
    /// ordre de grandeur que B2Brouter F05 §4.3).
    /// </summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);
}
