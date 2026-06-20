namespace Liakont.PaClients.ChorusPro.Wire;

using System.Text.Json.Serialization;

/// <summary>
/// Corps de la requête du service <c>consulterCR</c> (F18 §4) : l'accusé de réception du dépôt
/// (<c>numeroFluxDepot</c>, F18 §3.4) dont on relit l'état d'intégration. camelCase fixé explicitement
/// (voir <see cref="ChorusProConsulterCrResponse"/>). <c>internal</c> : ne fuit pas hors de l'assembly.
/// </summary>
internal sealed record ChorusProConsulterCrRequest
{
    /// <summary>Numéro de flux de dépôt (accusé de réception, F18 §3.4) à consulter.</summary>
    [JsonPropertyName("numeroFluxDepot")]
    public required string NumeroFluxDepot { get; init; }
}
