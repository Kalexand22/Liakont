namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Contexte de processus du document (EN 16931 BG-2, schéma <c>process_control</c> de l'OpenAPI).
/// Seul <c>specification_identifier</c> est requis par le schéma.
/// </summary>
internal sealed record SuperPdpEnProcessControl
{
    /// <summary>
    /// Identifiant de spécification (EN 16931 BT-24) : <c>urn:cen.eu:en16931:2017</c> — valeur normative
    /// de la norme, confirmée par la facture de test de la sandbox (F14 §3.2).
    /// </summary>
    public required string SpecificationIdentifier { get; init; }
}
