namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Réponse Super PDP à une émission / une relecture de facture (F14 §4). DTO PROPRIÉTAIRE, <c>internal</c>.
/// ⚠️ Une réponse HTTP 200 PEUT contenir un <see cref="Errors"/> non vide (erreur silencieuse — piège à
/// confirmer sandbox, F14 §4.1 / O6) : la présence d'erreurs est inspectée par le mapper INDÉPENDAMMENT
/// du code HTTP — jamais comptée « émise » à tort (CLAUDE.md n°3).
/// </summary>
internal sealed record SuperPdpInvoiceResponse
{
    /// <summary>Identifiant attribué par Super PDP, ou <c>null</c> si l'émission n'a pas abouti.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Numéro de document (EN 16931 BT-1) renvoyé par Super PDP. Clé d'unicité côté PA (F14 §4.1) : sert
    /// à RACCROCHER une facture déjà créée lors de la relecture d'idempotence après un échec transitoire.
    /// </summary>
    public string? Number { get; init; }

    /// <summary>État Super PDP du document (cible de conception : new / sending / issued / error — F14 §4).</summary>
    public string? State { get; init; }

    /// <summary>Erreurs remontées par Super PDP (même sur HTTP 200 — F14 §4.1), jamais perdues.</summary>
    public IReadOnlyList<SuperPdpError>? Errors { get; init; }

    /// <summary>Identifiants des tax reports liés (F14 §4).</summary>
    public IReadOnlyList<string>? TaxReportIds { get; init; }
}
