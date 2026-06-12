namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Le format d'erreur Super PDP (✅ confirmé sandbox 2026-06-12 — F14 §4.1) :
/// <c>{"http_status_code": 400, "message": "…"}</c>. Le message (souvent en français : règles
/// <c>BR-*</c> du converter, contrôles d'envoi) est conservé INTACT pour la piste d'audit et
/// l'opérateur (F06/DR6, CLAUDE.md n°12).
/// </summary>
internal sealed record SuperPdpError
{
    /// <summary>Le code HTTP répété dans le corps d'erreur.</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>Message d'erreur tel que retourné par Super PDP, jamais reformulé.</summary>
    public string? Message { get; init; }
}
