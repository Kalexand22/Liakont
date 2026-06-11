namespace Liakont.PaClients.SuperPdp.Wire;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Options de sérialisation JSON du plug-in Super PDP, mises en cache (CA1869). Convention snake_case :
/// la réponse OAuth standard utilise <c>access_token</c> / <c>token_type</c> / <c>expires_in</c> (✅
/// confirmée par le test réel du 2026-06-11), et le schéma métier snake_case est la CIBLE de conception
/// (F14 §3 ; à confirmer OpenAPI sandbox PAS03 — la lecture est insensible à la casse, donc tolérante).
/// Les champs nuls sont OMIS en écriture. System.Text.Json sérialise les <see cref="decimal"/> en
/// nombres JSON sans perte — aucun <c>double</c> sur un montant (CLAUDE.md n°1).
/// </summary>
internal static class SuperPdpJson
{
    /// <summary>Options partagées (insensibles à la casse en lecture, snake_case en écriture).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
