namespace Liakont.SignatureProviders.Yousign;

/// <summary>
/// Configuration RÉSOLUE d'un compte Yousign d'un tenant, prête à l'emploi par le provider (ADR-0029 §6).
/// Produite par un <see cref="IYousignAccountResolver"/> (implémenté par le Host, qui DÉCHIFFRE la clé API
/// et le secret webhook via le coffre du tenant — patron <c>DataProtectionSecretProtector</c>).
/// <para>
/// ⚠️ Porte la clé API ET le secret webhook EN CLAIR : objet de transport EN MÉMOIRE uniquement. Jamais
/// persisté, jamais journalisé (CLAUDE.md n°10) — <see cref="ToString"/> caviarde les secrets. L'URL de base
/// est dérivée de l'<see cref="YousignUrlAllowlist">allowlist par environnement</see>, jamais un champ libre.
/// </para>
/// </summary>
public sealed record YousignAccountConfig
{
    /// <summary>Crée une configuration de compte Yousign résolue.</summary>
    /// <param name="environment">Environnement du compte (sandbox / production) — décide l'URL de base allowlistée.</param>
    /// <param name="apiKey">Clé API en clair (déchiffrée par le Host) — transport mémoire, jamais persistée/journalisée.</param>
    /// <param name="webhookSecret">Secret HMAC du webhook en clair (déchiffré) — idem.</param>
    public YousignAccountConfig(YousignEnvironment environment, string apiKey, string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("La clé API Yousign est obligatoire.", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            throw new ArgumentException("Le secret de webhook Yousign est obligatoire.", nameof(webhookSecret));
        }

        Environment = environment;
        ApiKey = apiKey;
        WebhookSecret = webhookSecret;
    }

    /// <summary>Environnement du compte (sandbox / production).</summary>
    public YousignEnvironment Environment { get; }

    /// <summary>Clé API en clair (transport mémoire — ne jamais persister/journaliser).</summary>
    public string ApiKey { get; }

    /// <summary>Secret HMAC du webhook en clair (transport mémoire — ne jamais persister/journaliser).</summary>
    public string WebhookSecret { get; }

    /// <summary>URL de base de l'API, dérivée de l'allowlist (anti-SSRF) — jamais un champ tenant libre.</summary>
    public Uri BaseUri => YousignUrlAllowlist.ResolveBaseUri(Environment);

    /// <summary>Représentation CAVIARDÉE : ne révèle jamais les secrets (CLAUDE.md n°10).</summary>
    public override string ToString() =>
        $"YousignAccountConfig {{ Environment = {Environment}, ApiKey = ***, WebhookSecret = *** }}";
}
