namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Identifiant qualifié par un scheme ISO 6523 (schémas <c>legal_registration_identifier</c> /
/// <c>electronic_address</c> de l'OpenAPI — <c>value</c> et <c>scheme</c> requis). Pour un SIREN
/// français : scheme <c>0002</c> (✅ validé en sandbox, F14 §3.2).
/// </summary>
internal sealed record SuperPdpEnIdentifier
{
    /// <summary>La valeur de l'identifiant (ex. le SIREN).</summary>
    public required string Value { get; init; }

    /// <summary>Le scheme ISO 6523 qualifiant la valeur (ex. <c>0002</c> = SIREN).</summary>
    public required string Scheme { get; init; }
}
