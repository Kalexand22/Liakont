namespace Liakont.PaClients.ChorusPro;

/// <summary>
/// Configuration RÉSOLUE d'un compte Chorus Pro, prête à l'emploi par le client. Chorus Pro via PISTE
/// exige une DOUBLE authentification (F18 §2) : un jeton OAuth2 <c>client_credentials</c> du compte
/// <b>PISTE</b> (<see cref="PisteClientId"/> / <see cref="PisteClientSecret"/>) ET un compte technique
/// <b>Chorus Pro</b> DISTINCT (<see cref="TechnicalLogin"/> / <see cref="TechnicalPassword"/>, en-tête
/// <c>cpro-account</c>). Les URLs (<see cref="BaseUrl"/> = base API <c>cpro</c>, <see cref="TokenEndpoint"/>
/// = endpoint jeton OAuth2) sont FOURNIES par le Host depuis le paramétrage du tenant / déploiement —
/// F18 §3.3 « NE PAS HARDCODER » : elles se verrouillent au Swagger PISTE au raccordement.
/// <para>
/// Produite par un <see cref="IChorusProAccountResolver"/> (implémenté par le Host, qui déchiffre les
/// secrets via le coffre du tenant). ⚠️ Porte des SECRETS EN CLAIR (<see cref="PisteClientSecret"/>,
/// <see cref="TechnicalPassword"/>) : objet de transport EN MÉMOIRE uniquement, jamais persisté ni
/// journalisé (CLAUDE.md n°10) — <see cref="ToString"/> est redéfini pour CAVIARDER les secrets.
/// </para>
/// </summary>
public sealed record ChorusProAccountConfig
{
    /// <summary>Crée une configuration de compte Chorus Pro résolue. Lève si une valeur obligatoire manque (CLAUDE.md n°3).</summary>
    /// <param name="environment">Environnement du compte (qualification / production — F18 §2.1).</param>
    /// <param name="baseUrl">Base API Chorus Pro (<c>cpro</c>) verrouillée au raccordement (F18 §3.3). Absolue. Obligatoire.</param>
    /// <param name="tokenEndpoint">Endpoint jeton OAuth2 PISTE verrouillé au raccordement (F18 §2.1). Absolu. Obligatoire.</param>
    /// <param name="accountId">Identifiant de compte (audit + lectures). Obligatoire.</param>
    /// <param name="pisteClientId">Identifiant client OAuth2 PISTE (déchiffré par le Host). Obligatoire.</param>
    /// <param name="pisteClientSecret">Secret client OAuth2 PISTE, EN CLAIR (déchiffré par le Host). Obligatoire.</param>
    /// <param name="technicalLogin">Login du compte technique Chorus Pro (en-tête <c>cpro-account</c>, F18 §2.2). Obligatoire.</param>
    /// <param name="technicalPassword">Mot de passe du compte technique Chorus Pro, EN CLAIR (F18 §2.2). Obligatoire.</param>
    /// <param name="connectionEmail">E-mail de connexion du compte technique (résolution <c>idUtilisateurCourant</c>, F18 §3.2). Obligatoire.</param>
    public ChorusProAccountConfig(
        ChorusProEnvironment environment,
        Uri baseUrl,
        Uri tokenEndpoint,
        string accountId,
        string pisteClientId,
        string pisteClientSecret,
        string technicalLogin,
        string technicalPassword,
        string connectionEmail)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        RequireAbsolute(baseUrl, nameof(baseUrl));
        RequireAbsolute(tokenEndpoint, nameof(tokenEndpoint));
        RequireNonBlank(accountId, nameof(accountId), "L'identifiant de compte Chorus Pro est obligatoire.");
        RequireNonBlank(pisteClientId, nameof(pisteClientId), "Le client_id OAuth2 PISTE est obligatoire.");
        RequireNonBlank(pisteClientSecret, nameof(pisteClientSecret), "Le client_secret OAuth2 PISTE est obligatoire.");
        RequireNonBlank(technicalLogin, nameof(technicalLogin), "Le login du compte technique Chorus Pro est obligatoire.");
        RequireNonBlank(technicalPassword, nameof(technicalPassword), "Le mot de passe du compte technique Chorus Pro est obligatoire.");
        RequireNonBlank(connectionEmail, nameof(connectionEmail), "L'e-mail de connexion du compte technique Chorus Pro est obligatoire.");

        Environment = environment;

        // RFC 3986 relative-resolution: if BaseUrl has no trailing slash, new Uri(base, "factures/v1/deposer")
        // REPLACES the last path segment — e.g. "/cpro" becomes "/factures/v1/deposer", silently dropping /cpro.
        // Normalising here (F18 §3.3) ensures every relative business path always resolves UNDER the full base.
        BaseUrl = EnsureTrailingSlash(baseUrl);
        TokenEndpoint = tokenEndpoint;
        AccountId = accountId;
        PisteClientId = pisteClientId;
        PisteClientSecret = pisteClientSecret;
        TechnicalLogin = technicalLogin;
        TechnicalPassword = technicalPassword;
        ConnectionEmail = connectionEmail;
    }

    /// <summary>Environnement du compte (qualification / production).</summary>
    public ChorusProEnvironment Environment { get; }

    /// <summary>Base API Chorus Pro (<c>cpro</c>), verrouillée au raccordement (F18 §3.3, jamais en dur).</summary>
    public Uri BaseUrl { get; }

    /// <summary>Endpoint jeton OAuth2 PISTE, verrouillé au raccordement (F18 §2.1).</summary>
    public Uri TokenEndpoint { get; }

    /// <summary>Identifiant de compte Chorus Pro.</summary>
    public string AccountId { get; }

    /// <summary>Identifiant client OAuth2 PISTE (transport mémoire — ne jamais persister/journaliser).</summary>
    public string PisteClientId { get; }

    /// <summary>Secret client OAuth2 PISTE, EN CLAIR (transport mémoire — ne jamais persister/journaliser).</summary>
    public string PisteClientSecret { get; }

    /// <summary>Login du compte technique Chorus Pro (en-tête <c>cpro-account</c>).</summary>
    public string TechnicalLogin { get; }

    /// <summary>Mot de passe du compte technique Chorus Pro, EN CLAIR (transport mémoire — ne jamais persister/journaliser).</summary>
    public string TechnicalPassword { get; }

    /// <summary>E-mail de connexion du compte technique (résolution <c>idUtilisateurCourant</c>, F18 §3.2).</summary>
    public string ConnectionEmail { get; }

    /// <summary>Représentation CAVIARDÉE : ne révèle jamais les secrets (PISTE + compte technique) — CLAUDE.md n°10.</summary>
    public override string ToString() =>
        $"ChorusProAccountConfig {{ Environment = {Environment}, AccountId = {AccountId}, " +
        $"PisteClientId = ***, PisteClientSecret = ***, TechnicalLogin = ***, TechnicalPassword = *** }}";

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;

    private static void RequireAbsolute(Uri uri, string paramName)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("L'URL Chorus Pro doit être absolue (verrouillée au raccordement, F18 §3.3).", paramName);
        }
    }

    private static void RequireNonBlank(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }
}
