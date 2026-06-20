namespace Liakont.PaClients.ChorusPro;

/// <summary>
/// Constantes du plug-in Chorus Pro. Chaque valeur porte son STATUT de sourcing (même registre que
/// F15/F17/F18) — jamais une valeur inventée (CLAUDE.md n°2/15) :
/// <list type="bullet">
///   <item>✅ <b>sourcé</b> : <c>scope=openid</c> (ajout PISTE au <c>client_credentials</c>, F18 §2.1),
///   en-tête <c>cpro-account</c> (F18 §2.2), hôtes <c>*.piste.gouv.fr</c> (F18 §7).</item>
///   <item>🔶 <b>lecture courante</b> : endpoint jeton OAuth2 — à verrouiller au Swagger PISTE (F18 §2.1).</item>
///   <item>❓ <b>À VERROUILLER, NE PAS HARDCODER</b> : base API + chemin REST versionné — fournis par
///   compte (paramétrage du tenant / déploiement, jamais figés dans le code), F18 §3.3/§7.</item>
/// </list>
/// Les URLs réelles (base API, endpoint jeton) sont portées par <see cref="ChorusProAccountConfig"/>
/// (résolu par le Host) et NON figées ici — F18 §3.3 « ne pas hardcoder ». Le transport HTTP qui les
/// consomme est livré à partir de CP03.
/// </summary>
public static class ChorusProDefaults
{
    /// <summary>Clé de registre du plug-in (résolution par <see cref="Modules.Transmission.Contracts.PaAccountDescriptor.PaType"/>, insensible à la casse).</summary>
    public const string PaTypeKey = "ChorusPro";

    /// <summary>Nom affichable de la PA, porté dans les messages opérateur français (CLAUDE.md n°12).</summary>
    public const string PaName = "Chorus Pro";

    /// <summary>Nom du client HTTP nommé enregistré via <c>AddHttpClient</c> (F18 §7).</summary>
    public const string HttpClientName = "Liakont.PaClients.ChorusPro";

    /// <summary>
    /// Scope OAuth2 ajouté par PISTE au <c>client_credentials</c> standard (✅ sourcé F18 §2.1 :
    /// <c>scope=openid</c> — « réussir son raccordement API OAuth2 », communauté Chorus Pro/PISTE).
    /// </summary>
    public const string TokenScope = "openid";

    /// <summary>
    /// Nom de l'en-tête HTTP du compte technique Chorus Pro (✅ sourcé F18 §2.2 :
    /// <c>cpro-account: base64(login:motDePasse)</c>, DISTINCT du compte PISTE). Sa valeur est un SECRET
    /// (jamais journalisée — CLAUDE.md n°10).
    /// </summary>
    public const string TechnicalAccountHeaderName = "cpro-account";

    /// <summary>
    /// Chemin REST RELATIF (sous <see cref="ChorusProAccountConfig.BaseUrl"/>) du dépôt de flux
    /// <c>deposerFluxFacture</c> — 🔶 <b>à verrouiller au Swagger PISTE courant</b> (F18 §3.3, gabarit
    /// versionné <c>/cpro/factures/v1/…</c>). Le « NE PAS HARDCODER » de §3.3 vise la <b>base API</b>
    /// (host + <c>/cpro/</c>, qualif vs prod) — celle-ci est portée par <see cref="ChorusProAccountConfig.BaseUrl"/>
    /// (paramétrage du tenant). La ressource versionnée, elle, fait partie du CONTRAT d'API (identique pour
    /// tous les comptes d'une version donnée — même statut que les chemins <c>v1.beta/invoices</c> de
    /// Super PDP), donc constante de code ; provisoire tant que le raccordement ne l'a pas verrouillée.
    /// </summary>
    public const string DepositPath = "factures/v1/deposer";

    /// <summary>
    /// Valeur de <c>syntaxeFlux</c> pour un Factur-X (CII embarqué dans un PDF/A-3) — ✅ sourcé F18 §3.1
    /// (Spec V5.00, nomenclature des syntaxes de flux). Constante stable, jamais inventée (CLAUDE.md n°2).
    /// </summary>
    public const string SyntaxeFluxFacturX = "IN_DP_E2_CII_FACTURX";

    /// <summary>
    /// Drapeau <c>avecSignature</c> du dépôt : <c>false</c> — notre artefact scellé est un PDF/A-3 conforme
    /// NON signé (✅ décision interne D9, F18 §6 ; ❓ acceptation Chorus Pro d'un Factur-X non signé à
    /// confirmer au raccordement). Reflète l'artefact, jamais une signature qu'on n'applique pas.
    /// </summary>
    public const bool DepositWithSignature = false;

    /// <summary>
    /// Marge de sécurité retranchée à <c>expires_in</c> avant de considérer le jeton PISTE expiré : le
    /// jeton est renouvelé un peu AVANT son échéance réelle (F18 §2.1 — piloter sur l'<c>expires_in</c>
    /// réel renvoyé, jamais figer « 3600 s »). Modèle technique : <c>SuperPdpTokenProvider</c>.
    /// </summary>
    public static readonly TimeSpan TokenExpirySkew = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Délai d'attente HTTP par appel : 60 s (le dépôt de flux peut être lent — F18 §7, même ordre de
    /// grandeur que Super PDP F14 §7 / B2Brouter F05 §4.3).
    /// </summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Construit le <c>nomFichier</c> du flux déposé à partir du numéro de document (BT-1), assaini pour un
    /// nom de fichier sûr (le contenu reste l'artefact opaque ; aucun montant ni donnée fiscale manipulés).
    /// </summary>
    /// <param name="documentNumber">Numéro de document (BT-1). Caractères non alphanumériques remplacés.</param>
    public static string FileNameFor(string documentNumber)
    {
        var safe = string.IsNullOrWhiteSpace(documentNumber)
            ? "document"
            : string.Concat(documentNumber.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"factur-x_{safe}.pdf";
    }
}
