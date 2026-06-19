namespace Liakont.SignatureProviders.Yousign;

using System.Net.Http;
using System.Net.Http.Headers;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Fabrique du provider Yousign (ADR-0027 §4 ; ADR-0029). Découverte par l'<c>ISignatureProviderRegistry</c>
/// du module Signature via son <see cref="ProviderType"/> — aucun <c>if (type == "Yousign")</c> ailleurs
/// (CLAUDE.md n°6/16). Construit un <see cref="YousignSignatureProvider"/> POUR le compte d'un tenant : elle
/// RÉSOUT les secrets chiffrés (clé API + secret webhook) via <see cref="IYousignAccountResolver"/> — implémenté
/// par le Host, seul détenteur du coffre — puis configure le client HTTP nommé (URL de base ALLOWLISTÉE +
/// Bearer en mémoire, jamais journalisé).
/// </summary>
public sealed class YousignSignatureProviderFactory : ISignatureProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IYousignAccountResolver _accountResolver;

    public YousignSignatureProviderFactory(
        IHttpClientFactory httpClientFactory,
        IYousignAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(accountResolver);
        _httpClientFactory = httpClientFactory;
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string ProviderType => YousignDefaults.ProviderType;

    /// <inheritdoc />
    public ISignatureProvider Create(SignatureProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var config = _accountResolver.Resolve(account);

        // Client HTTP nommé (handler anti-SSRF + AllowAutoRedirect=false partagé), configuré POUR CE COMPTE :
        // URL de base ALLOWLISTÉE (jamais un champ tenant) + Bearer en mémoire (jamais journalisé — CLAUDE.md n°10).
        // La clé API peut être absente (compte inbound-only) : le header Authorization n'est posé que si présente.
        var httpClient = _httpClientFactory.CreateClient(YousignDefaults.HttpClientName);
        httpClient.BaseAddress = config.BaseUri;
        httpClient.Timeout = YousignDefaults.HttpTimeout;
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        return new YousignSignatureProvider(httpClient, config);
    }
}
