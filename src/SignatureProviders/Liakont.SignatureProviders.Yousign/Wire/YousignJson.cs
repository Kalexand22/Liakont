namespace Liakont.SignatureProviders.Yousign.Wire;

using System.Text.Json;

/// <summary>
/// Options de (dé)sérialisation des types « fil » Yousign (API v3, snake_case). Internes au plug-in : aucun
/// type Yousign ne traverse <c>ISignatureProvider</c> (INV-YOUSIGN-2).
/// </summary>
internal static class YousignJson
{
    /// <summary>Options partagées (snake_case, insensible à la casse en lecture, propriétés nulles ignorées en écriture).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
