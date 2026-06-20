namespace Liakont.PaClients.ChorusPro.Wire;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Options de sérialisation JSON du plug-in Chorus Pro, mises en cache (CA1869). La réponse OAuth2 standard
/// utilise <c>access_token</c> / <c>token_type</c> / <c>expires_in</c> (forme OAuth 2.0 RFC 6749 §5.1, à
/// verrouiller au Swagger PISTE — F18 §2.1). Lecture insensible à la casse ; champs nuls omis en écriture.
/// <c>internal</c> : aucun DTO « fil » Chorus Pro n'est exposé hors de l'assembly (acceptance CP02,
/// vérifié par <c>ChorusProBoundaryTests</c>).
/// </summary>
internal static class ChorusProJson
{
    /// <summary>Options partagées (insensibles à la casse en lecture, snake_case en écriture).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
