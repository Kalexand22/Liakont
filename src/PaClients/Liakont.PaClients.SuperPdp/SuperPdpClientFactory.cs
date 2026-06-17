namespace Liakont.PaClients.SuperPdp;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in Super PDP (ajouter-un-plugin-pa §3) : un client = un compte PA de tenant
/// (identifiants OAuth + identifiant de compte + environnement — F14 §3.1/§7). Le
/// <see cref="IPaClientRegistry"/> du module Transmission l'indexe par <see cref="PaType"/> ; la
/// résolution d'un compte se fait par la CLÉ, jamais par un <c>if (pa is SuperPdp)</c> (CLAUDE.md n°6/16).
/// Enregistrée en singleton (capturée par le registre singleton) ; elle crée un client par compte à la
/// demande, avec son fournisseur de jeton OAuth dédié (cache de jeton par compte).
/// </summary>
public sealed class SuperPdpClientFactory : IPaClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISuperPdpAccountResolver _accountResolver;

    /// <summary>Construit la fabrique.</summary>
    /// <param name="httpClientFactory">
    /// Fabrique du client HTTP nommé (<see cref="SuperPdpDefaults.HttpClientName"/>) configuré pour
    /// TLS 1.2/1.3 par <see cref="SuperPdpPaClientRegistration.AddSuperPdpPaClient"/>.
    /// </param>
    /// <param name="accountResolver">
    /// Résout le descripteur non sensible du tenant vers les identifiants OAuth déchiffrés (frontière
    /// plug-in ↔ coffre du tenant — voir <see cref="ISuperPdpAccountResolver"/>).
    /// </param>
    public SuperPdpClientFactory(IHttpClientFactory httpClientFactory, ISuperPdpAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(accountResolver);
        _httpClientFactory = httpClientFactory;
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string PaType => SuperPdpDefaults.PaTypeKey;

    /// <inheritdoc />
    public PaAuthMode AuthMode => PaAuthMode.OAuth2ClientCredentials;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var config = _accountResolver.Resolve(account);
        var baseUrl = config.BaseUrl;

        // Client HTTP nommé (handler TLS 1.2/1.3 partagé/poolé), configuré pour CE compte : URL de base
        // (les chemins métier sont relatifs) + délai (F14 §7). Les secrets OAuth ne vivent que sur le
        // fournisseur de jeton en mémoire (jamais journalisés — CLAUDE.md n°10).
        var httpClient = _httpClientFactory.CreateClient(SuperPdpDefaults.HttpClientName);
        httpClient.BaseAddress = baseUrl;
        httpClient.Timeout = SuperPdpDefaults.HttpTimeout;

        // Le token-endpoint est ABSOLU (hors préfixe de version) : on le construit depuis la base du compte
        // pour qu'il suive l'environnement, sans dépendre de la BaseAddress du client métier.
        var tokenEndpoint = new Uri(baseUrl, SuperPdpDefaults.TokenPath);
        var tokenProvider = new SuperPdpTokenProvider(
            httpClient, tokenEndpoint, config.ClientId, config.ClientSecret);

        return new SuperPdpClient(httpClient, tokenProvider, new SuperPdpClientOptions(config.AccountId));
    }
}
