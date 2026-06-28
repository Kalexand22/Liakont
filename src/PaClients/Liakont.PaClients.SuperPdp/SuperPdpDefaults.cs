namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Constantes du plug-in Super PDP. Chaque valeur porte son STATUT de vérification (F14 §2/§12) —
/// jamais une valeur inventée (CLAUDE.md n°2/15). Centralisées ici pour qu'un changement d'API (URL,
/// préfixe de version, chemin) tienne en un seul point.
/// <list type="bullet">
///   <item>✅ <b>confirmés</b> : prefixe de version <c>v1.beta</c>, token-endpoint <c>oauth2/token</c>,
///   base <c>api.superpdp.tech</c>, émission <c>POST /v1.beta/invoices</c> (XML CII/UBL ou PDF Factur-X
///   uniquement — JAMAIS de JSON), conversion <c>POST /v1.beta/invoices/convert</c>, relecture
///   <c>GET /v1.beta/invoices/{id}</c> — OpenAPI officielle v1.24.0.beta
///   (<c>api.superpdp.tech/openapi/superpdp.json</c>) + envois sandbox réels du 2026-06-12 (F14 §3.2/§3.4,
///   en-tête de <c>orchestration/items/PAS.yaml</c>).</item>
///   <item>🟠 <b>restent à confirmer</b> : tax reports / settings / compte (F14 §12 O2 partiel).</item>
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
    /// Préfixe de version des endpoints métier (✅ confirmé OpenAPI + sandbox). Sans barre de début/fin :
    /// combiné aux chemins relatifs ci-dessous.
    /// </summary>
    public const string ApiVersionPrefix = "v1.beta";

    /// <summary>
    /// Chemin du token-endpoint OAuth 2.0 (✅ confirmé : <c>POST &lt;base&gt;/oauth2/token</c>,
    /// <c>grant_type=client_credentials</c> → bearer, test réel du 2026-06-11). HORS préfixe de version.
    /// </summary>
    public const string TokenPath = "oauth2/token";

    /// <summary>
    /// Chemin relatif d'émission de document (✅ confirmé sandbox 2026-06-12 : <c>POST /v1.beta/invoices</c>
    /// en <c>application/xml</c> [CII/UBL] — F14 §3.2). Relatif au préfixe de version.
    /// </summary>
    public const string InvoicesPath = "invoices";

    /// <summary>
    /// Chemin relatif d'envoi des transactions e-reporting B2C (✅ POST confirmé sandbox 2026-06-22 :
    /// <c>POST /v1.beta/b2c_transactions</c>, body JSON <c>{ data: [ b2c_transaction ] }</c>, id serveur
    /// assigné). Relatif au préfixe de version.
    /// </summary>
    public const string B2cTransactionsPath = "b2c_transactions";

    /// <summary>
    /// Chemin relatif de l'entreprise liée au compte OAuth (✅ endpoint confirmé sandbox 2026-06-12 :
    /// <c>GET /v1.beta/companies/me</c> → <c>{ number, formal_name, … }</c> — F14 §3.2,
    /// <c>SuperPdpSandboxTests</c>). Sert à LIRE l'état de publication du SIREN : Super PDP n'expose pas de
    /// <c>tax_report_setting</c> éditable (la vérification KYC de l'entreprise est faite dans l'espace Super
    /// PDP) — l'entreprise présente avec un <c>number</c>/SIREN = transmission active. Relatif au préfixe de version.
    /// </summary>
    public const string CompaniesMePath = "companies/me";

    /// <summary>
    /// Chemin relatif de CONVERSION de format (✅ confirmé sandbox 2026-06-12 :
    /// <c>POST /v1.beta/invoices/convert?from=en16931&amp;to=cii</c> — F14 §3.2). Le converter applique les
    /// règles de validation EN 16931 officielles (<c>BR-*</c>). Relatif au préfixe de version.
    /// </summary>
    public const string ConvertPath = "invoices/convert";

    /// <summary>Format source de la conversion : le JSON <c>en16931</c> (schéma <c>en_invoice</c>, F14 §3.2).</summary>
    public const string ConvertFromFormat = "en16931";

    /// <summary>Format cible de la conversion : XML CII (accepté par l'émission, F14 §3.2).</summary>
    public const string ConvertToFormat = "cii";

    /// <summary>
    /// Type de document UNTDID 1001 d'une facture commerciale (EN 16931 BT-3) : <c>380</c>, en NOMBRE
    /// JSON — ✅ valeur ET type de la facture de test de la sandbox (l'API rejette une chaîne :
    /// « cannot unmarshal JSON string into Go model.InvoiceTypeCode », constaté 2026-06-12 — F14 §3.2).
    /// Les avoirs (<c>381</c>) ne sont pas émis en V1 (capacité <c>SupportsCreditNotes</c> = false, F14 §5).
    /// </summary>
    public const int CommercialInvoiceTypeCode = 380;

    /// <summary>
    /// Identifiant de spécification EN 16931 (BT-24) : <c>urn:cen.eu:en16931:2017</c> — valeur normative,
    /// ✅ confirmée par la facture de test de la sandbox (F14 §3.2).
    /// </summary>
    public const string SpecificationIdentifier = "urn:cen.eu:en16931:2017";

    /// <summary>Scheme ISO 6523 du SIREN (<c>0002</c>) — ✅ adressage validé en sandbox (F14 §3.2).</summary>
    public const string SirenScheme = "0002";

    /// <summary>
    /// Unité neutre UN/ECE Rec 20 « one » (<c>C62</c>, EN 16931 BT-130) émise quand le pivot ne porte pas
    /// d'unité — ✅ valeur de la facture de test de la sandbox, cohérente avec la quantité 1 émise
    /// (cf. <c>SuperPdpPayloadBuilder</c>).
    /// </summary>
    public const string DefaultQuantityUnitCode = "C62";

    /// <summary>
    /// Longueur maximale de l'<c>external_id</c> accepté par <c>POST /v1.beta/invoices</c> (✅ OpenAPI :
    /// <c>maxLength: 36</c>) — clé d'idempotence portée par le numéro de document (F14 §4.1).
    /// </summary>
    public const int ExternalIdMaxLength = 36;

    /// <summary>
    /// Base d'API SANDBOX (✅ confirmée par les tests réels : <c>api.superpdp.tech</c>).
    /// <para>
    /// ⛔ La base PRODUCTION n'est PAS confirmée (F14 §12 O1 — Super PDP peut exposer un hôte prod distinct
    /// ou utiliser le même hôte avec des comptes séparés : non tranché). On n'invente PAS un hôte fictif
    /// (CLAUDE.md n°15) : un compte configuré en <c>Production</c> est BLOQUÉ à la construction
    /// (<see cref="SuperPdpAccountConfig.BaseUrl"/> lève <see cref="NotSupportedException"/>) jusqu'à ce que
    /// la base de production soit confirmée — bloquer plutôt qu'envoyer faux (CLAUDE.md n°3).
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
