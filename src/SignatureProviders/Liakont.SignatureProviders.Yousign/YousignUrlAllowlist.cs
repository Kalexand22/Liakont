namespace Liakont.SignatureProviders.Yousign;

/// <summary>
/// Allowlist anti-SSRF des origines d'appel sortant Yousign (ADR-0029 §6 ; INV-YOUSIGN-7). L'URL de base
/// N'EST JAMAIS un champ tenant libre : le tenant ne choisit qu'entre des <see cref="YousignEnvironment"/>
/// CONNUS, chacun lié à une ORIGINE <c>https://</c> EXACTE (schéma + host + port). Le plug-in REJETTE
/// (a) tout schéma non-HTTPS, (b) toute origine hors liste, (c) — via <see cref="YousignSsrfGuardHandler"/>
/// + <c>AllowAutoRedirect = false</c> — toute redirection (3xx) vers une cible non listée. Sinon un admin
/// tenant (ou un <c>http://</c> / une redirection) ferait émettre des appels AUTHENTIFIÉS (porteurs de la
/// clé API Bearer) vers une adresse interne/arbitraire = SSRF + fuite de la clé API.
/// </summary>
public static class YousignUrlAllowlist
{
    /// <summary>Base de chemin de l'API v3, ajoutée après l'origine allowlistée.</summary>
    public const string ApiV3Path = "/v3/";

    // Origines https EXACTES par environnement (API Yousign Public v3 — ADR-0029 §6). Figées dans le code.
    private static readonly IReadOnlyDictionary<YousignEnvironment, Uri> Origins =
        new Dictionary<YousignEnvironment, Uri>
        {
            [YousignEnvironment.Sandbox] = new Uri("https://api-sandbox.yousign.app", UriKind.Absolute),
            [YousignEnvironment.Production] = new Uri("https://api.yousign.app", UriKind.Absolute),
        };

    /// <summary>
    /// URL de base complète (origine allowlistée + segment <c>/v3/</c>) pour un environnement. C'est la seule
    /// source de l'URL de base — jamais un champ tenant.
    /// </summary>
    /// <param name="environment">Environnement déclaré du compte.</param>
    public static Uri ResolveBaseUri(YousignEnvironment environment)
    {
        if (!Origins.TryGetValue(environment, out var origin))
        {
            throw new InvalidOperationException(
                $"Environnement Yousign inconnu « {environment} » : aucune origine allowlistée. "
                + "L'URL de base ne peut pas être dérivée (anti-SSRF, ADR-0029 §6).");
        }

        return new Uri(origin, ApiV3Path);
    }

    /// <summary>
    /// Vrai si l'URI est absolue, en <c>https</c>, et que son origine (schéma + host + port) correspond
    /// EXACTEMENT à une origine allowlistée. Tout le reste (autre schéma, autre host, port différent) est
    /// refusé. Utilisé pour valider CHAQUE saut, y compris une cible de redirection (INV-YOUSIGN-7).
    /// </summary>
    /// <param name="uri">URI à valider (peut être <c>null</c> → refusée).</param>
    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var origin in Origins.Values)
        {
            if (string.Equals(uri.Scheme, origin.Scheme, StringComparison.Ordinal)
                && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == origin.Port)
            {
                return true;
            }
        }

        return false;
    }
}
