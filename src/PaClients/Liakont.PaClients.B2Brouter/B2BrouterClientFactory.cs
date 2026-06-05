namespace Liakont.PaClients.B2Brouter;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in B2Brouter (ajouter-un-plugin-pa §3) : un client = un compte PA de tenant
/// (clé API + identifiant de compte + URL — F05 §4.4). Le <see cref="IPaClientRegistry"/> du module
/// Transmission l'indexe par <see cref="PaType"/> ; la résolution d'un compte se fait par la CLÉ,
/// jamais par un <c>if (pa is B2Brouter)</c> (CLAUDE.md n°6/16). Enregistrée en singleton (capturée
/// par le registre singleton) ; elle crée un client par compte à la demande.
/// </summary>
public sealed class B2BrouterClientFactory : IPaClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IB2BrouterAccountResolver _accountResolver;

    /// <summary>Construit la fabrique.</summary>
    /// <param name="httpClientFactory">
    /// Fabrique du client HTTP nommé (<see cref="B2BrouterDefaults.HttpClientName"/>) configuré pour
    /// TLS 1.2/1.3 par <see cref="B2BrouterPaClientRegistration.AddB2BrouterPaClient"/>.
    /// </param>
    /// <param name="accountResolver">
    /// Résout le descripteur non sensible du tenant vers les identifiants déchiffrés (frontière
    /// plug-in ↔ coffre du tenant — voir <see cref="IB2BrouterAccountResolver"/>).
    /// </param>
    public B2BrouterClientFactory(IHttpClientFactory httpClientFactory, IB2BrouterAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(accountResolver);
        _httpClientFactory = httpClientFactory;
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string PaType => B2BrouterDefaults.PaTypeKey;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var config = _accountResolver.Resolve(account);

        // Client HTTP nommé (handler TLS 1.2/1.3 partagé/poolé), configuré pour CE compte : URL de base,
        // délai (F05 §4.3) et en-têtes d'auth/version (F05 §2). La clé API ne vit que sur l'instance
        // HttpClient en mémoire (jamais journalisée — CLAUDE.md n°10). On instancie un client par compte
        // (long-lived on-premise) : le handler reste mutualisé via la fabrique, la rotation de handler
        // par défaut ne s'applique pas à ces clients par compte — compromis assumé pour B2C on-premise.
        var httpClient = _httpClientFactory.CreateClient(B2BrouterDefaults.HttpClientName);
        httpClient.BaseAddress = config.BaseUrl;
        httpClient.Timeout = B2BrouterDefaults.HttpTimeout;
        httpClient.DefaultRequestHeaders.Add(B2BrouterDefaults.ApiKeyHeader, config.ApiKey);
        httpClient.DefaultRequestHeaders.Add(B2BrouterDefaults.ApiVersionHeader, config.ApiVersion);

        return new B2BrouterClient(httpClient, new B2BrouterClientOptions(config.AccountId));
    }
}
