namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Tax report B2Brouter tel que LU (F05 §2 : <c>GET /accounts/{id}/tax_reports.json</c> et
/// <c>GET /tax_reports/{id}.json</c>). DTO PROPRIÉTAIRE, <c>internal</c> : il ne fuit jamais hors de
/// l'assembly (acceptance PAB01, B2BrouterBoundaryTests). Désérialisé en snake_case
/// (<see cref="B2BrouterJson"/>) — <c>xml_base64</c>, <c>has_errors</c>. Le <c>xml_base64</c> n'est
/// présent qu'après génération du ledger DGFiP (batch ~02:00) : son absence = « pas encore généré »,
/// PAS une erreur (acceptance PAB03). Aucun montant n'est mappé (ils restent dans la réponse brute —
/// jamais un <c>double</c> sur un montant, CLAUDE.md n°1).
/// </summary>
internal sealed record B2BrouterTaxReport
{
    /// <summary>Identifiant du tax report côté B2Brouter.</summary>
    public string? Id { get; init; }

    /// <summary>Type de tax report tel que nommé par B2Brouter.</summary>
    public string? Type { get; init; }

    /// <summary>Transport / canal déclaré, ou <c>null</c> si absent.</summary>
    public string? Transport { get; init; }

    /// <summary>État B2Brouter (<c>new</c> / <c>sent</c> / <c>acknowledged</c> / <c>registered</c>, F05 §3).</summary>
    public string? State { get; init; }

    /// <summary>XML du ledger en base64, ou <c>null</c> tant qu'il n'est pas généré (F05 §2).</summary>
    public string? XmlBase64 { get; init; }

    /// <summary>Vrai si B2Brouter signale des erreurs sur ce tax report.</summary>
    public bool? HasErrors { get; init; }
}
