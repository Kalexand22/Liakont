namespace Liakont.SignatureProviders.Yousign;

using System.Net.Http;

/// <summary>
/// Handler de message HTTP qui REFUSE tout appel sortant dont l'URI n'est pas dans
/// <see cref="YousignUrlAllowlist"/> (ADR-0029 §6 ; INV-YOUSIGN-7). Défense en profondeur de l'anti-SSRF :
/// même si une URI arbitraire (ou une cible de redirection, <c>AllowAutoRedirect = false</c> sur le handler
/// primaire) parvenait jusqu'ici, elle est bloquée AVANT que la requête authentifiée (Bearer) ne parte. Une
/// URI hors liste lève une <see cref="InvalidOperationException"/> (on bloque plutôt que de fuiter la clé —
/// CLAUDE.md n°3/10), jamais un appel silencieux.
/// </summary>
internal sealed class YousignSsrfGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!YousignUrlAllowlist.IsAllowed(request.RequestUri))
        {
            throw new InvalidOperationException(
                $"Appel Yousign refusé (anti-SSRF, ADR-0029 §6) : l'URI « {request.RequestUri} » n'est pas "
                + "une origine HTTPS allowlistée. Seules les origines Yousign sandbox/production sont autorisées.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
