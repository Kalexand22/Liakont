namespace Liakont.PaClients.ChorusPro;

using System.Net.Http;
using System.Text;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in Chorus Pro (ajouter-un-plugin-pa §3) : un client = un compte PA de tenant
/// (creds PISTE + compte technique + URLs verrouillées — F18 §2/§3.3). Le <see cref="IPaClientRegistry"/>
/// du module Transmission l'indexe par <see cref="PaType"/> ; la résolution d'un compte se fait par la
/// CLÉ, jamais par un <c>if (pa is ChorusPro)</c> (CLAUDE.md n°6/16). Enregistrée en singleton ; elle crée
/// un client par compte à la demande, avec son fournisseur de jeton OAuth2 PISTE dédié (cache par compte).
/// <para>
/// CP03 — la fabrique exerce la frontière <see cref="IChorusProAccountResolver"/> (le Host déchiffre les
/// secrets du coffre du tenant) pour échouer TÔT sur un compte mal configuré (CLAUDE.md n°3), puis câble la
/// DOUBLE authentification PISTE : client HTTP nommé (TLS 1.2/1.3, <see cref="ChorusProPaClientRegistration"/>)
/// + <see cref="ChorusProTokenProvider"/> (Bearer) + en-tête <c>cpro-account</c> pré-calculé (compte
/// technique). Le transport métier (dépôt <c>deposerFluxFacture</c>, relecture <c>consulterCR</c>) arrive
/// avec CP04+ et s'appuiera sur cette auth. Les secrets ne vivent qu'en mémoire (jamais journalisés —
/// CLAUDE.md n°10).
/// </para>
/// </summary>
public sealed class ChorusProClientFactory : IPaClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChorusProAccountResolver _accountResolver;

    /// <summary>Construit la fabrique.</summary>
    /// <param name="httpClientFactory">
    /// Fabrique du client HTTP nommé (<see cref="ChorusProDefaults.HttpClientName"/>) configuré pour
    /// TLS 1.2/1.3 par <see cref="ChorusProPaClientRegistration.AddChorusProPaClient"/>.
    /// </param>
    /// <param name="accountResolver">
    /// Résout le descripteur non sensible du tenant vers les identifiants déchiffrés (frontière
    /// plug-in ↔ coffre du tenant — voir <see cref="IChorusProAccountResolver"/>).
    /// </param>
    public ChorusProClientFactory(IHttpClientFactory httpClientFactory, IChorusProAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(accountResolver);
        _httpClientFactory = httpClientFactory;
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string PaType => ChorusProDefaults.PaTypeKey;

    /// <inheritdoc />
    public PaAuthMode AuthMode => PaAuthMode.OAuth2WithTechnicalAccount;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Résout et VALIDE la configuration du compte via la frontière resolver : la résolution lève si le
        // compte est inconnu ou si un secret / une URL manque (le constructeur de ChorusProAccountConfig
        // valide) — on échoue TÔT sur un compte mal configuré (CLAUDE.md n°3, bloquer plutôt qu'envoyer faux).
        var config = _accountResolver.Resolve(account);

        // Client HTTP nommé (handler TLS 1.2/1.3 partagé/poolé), configuré pour CE compte : base API (les
        // chemins métier sont relatifs, livrés par CP04+) + délai (F18 §7). Les secrets OAuth ne vivent que
        // sur le fournisseur de jeton en mémoire ; le mot de passe du compte technique n'y transite jamais
        // en clair — seul son en-tête cpro-account pré-calculé est porté par le client (CLAUDE.md n°10).
        var httpClient = _httpClientFactory.CreateClient(ChorusProDefaults.HttpClientName);
        httpClient.BaseAddress = config.BaseUrl;
        httpClient.Timeout = ChorusProDefaults.HttpTimeout;

        var tokenProvider = new ChorusProTokenProvider(
            httpClient, config.TokenEndpoint, config.PisteClientId, config.PisteClientSecret, ChorusProDefaults.TokenScope);

        return new ChorusProClient(
            httpClient, tokenProvider, BuildTechnicalAccountHeader(config), ChorusProCapabilities.Declared);
    }

    // En-tête cpro-account = base64(login:motDePasse) du compte technique Chorus Pro (✅ sourcé F18 §2.2),
    // pré-calculé une fois par compte et constant. DISTINCT du Bearer PISTE. Valeur SECRÈTE (base64 n'est
    // PAS du chiffrement — jamais journalisée, CLAUDE.md n°10).
    private static string BuildTechnicalAccountHeader(ChorusProAccountConfig config) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{config.TechnicalLogin}:{config.TechnicalPassword}"));
}
