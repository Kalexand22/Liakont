namespace Liakont.PaClients.ChorusPro.Wire;

using System.Text.Json.Serialization;

/// <summary>
/// DTO « fil » de la réponse du service <c>consulterCR</c> (compte rendu d'intégration, F18 §4). Le seul
/// champ exploité est <c>etatCourantFlux</c> (9 libellés EXACTS accentués/casse mixte — F18 §4), mappé sur
/// <see cref="Modules.Transmission.Contracts.PaSendState"/> par <see cref="ChorusProStatusMapper"/>.
/// <para>
/// ⚠️ Les champs métier Chorus Pro sont en <b>camelCase</b> (<c>etatCourantFlux</c>) alors que
/// <see cref="ChorusProJson.Options"/> sérialise en <c>snake_case</c> (forme de la réponse OAuth2) :
/// le nom est donc fixé EXPLICITEMENT par <see cref="JsonPropertyNameAttribute"/> (prioritaire sur la
/// politique de nommage) — sans lui, la désérialisation laisserait la propriété nulle silencieusement.
/// </para>
/// <c>internal</c> : aucun DTO « fil » Chorus Pro n'est exposé hors de l'assembly (acceptance CP02,
/// vérifié par <c>ChorusProBoundaryTests</c>).
/// </summary>
internal sealed record ChorusProConsulterCrResponse
{
    /// <summary>Libellé <c>etatCourantFlux</c> renvoyé par Chorus Pro (F18 §4), ou <c>null</c> si absent.</summary>
    [JsonPropertyName("etatCourantFlux")]
    public string? EtatCourantFlux { get; init; }
}
