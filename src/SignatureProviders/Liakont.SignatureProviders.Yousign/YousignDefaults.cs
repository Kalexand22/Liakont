namespace Liakont.SignatureProviders.Yousign;

/// <summary>
/// Constantes du plug-in Yousign (ADR-0029). Toutes FIGÉES dans le code, jamais en configuration tenant
/// (CLAUDE.md n°7 ; anti-SSRF INV-YOUSIGN-7). Les URL de base sont gérées par <see cref="YousignUrlAllowlist"/>.
/// </summary>
public static class YousignDefaults
{
    /// <summary>Clé de registre du plug-in (insensible à la casse) — ADR-0027 §4.</summary>
    public const string ProviderType = "Yousign";

    /// <summary>Nom porté dans les messages opérateur (français).</summary>
    public const string ProviderName = "Yousign";

    /// <summary>Nom du client HTTP nommé enregistré via <c>AddHttpClient</c> (handler anti-SSRF partagé).</summary>
    public const string HttpClientName = "Liakont.SignatureProviders.Yousign";

    /// <summary>
    /// En-tête HMAC-SHA256 du webhook Yousign v3 (ADR-0029 §3). La vérification se fait sur le RAW body,
    /// à temps constant (jamais <c>string.Equals</c> sur l'hex).
    /// </summary>
    public const string WebhookSignatureHeader = "X-Yousign-Signature-256";

    /// <summary>Préfixe de l'en-tête de signature (HMAC hex minuscule, modèle <c>sha256=…</c>).</summary>
    public const string WebhookSignaturePrefix = "sha256=";

    /// <summary>Délai d'attente HTTP des appels sortants (anti-blocage du drain et du handler webhook).</summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);
}
