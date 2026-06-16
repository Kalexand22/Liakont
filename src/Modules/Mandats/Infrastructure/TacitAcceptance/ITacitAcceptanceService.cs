namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

/// <summary>
/// Service de bascule tacite des acceptations d'auto-factures (MND04, ADR-0024 §4) pour le tenant courant.
/// Exécuté une fois par tenant actif par <c>TenantJobRunner</c> (SOL06) — il ne fait JAMAIS sa propre boucle
/// multi-tenant (le fan-out est la seule responsabilité du runner, module-rules §6).
/// </summary>
internal interface ITacitAcceptanceService
{
    /// <summary>
    /// Bascule en <c>TacitlyAccepted</c> les acceptations en attente dont l'échéance est échue
    /// (<c>now ≥ DeadlineUtc</c>, <c>DeadlineUtc</c> non null ≡ mandat écrit ET délai non null). Depuis SIG05,
    /// l'acceptation est projetée via le module générique DocumentApproval ; chaque bascule écrit une transition
    /// SYSTÈME (operator_id null) atomiquement dans <c>documentapproval.document_approval_log</c> (INV-ACCEPT-5
    /// amendé). L'éligibilité est re-vérifiée sous verrou (anti-TOCTOU).
    /// </summary>
    Task<TacitAcceptanceRunResult> ProcessDueAsync(CancellationToken ct = default);
}

/// <summary>Bilan d'un passage du service pour un tenant.</summary>
/// <param name="Evaluated">Nombre de candidats énumérés (pré-filtrés dus à l'instant du balayage).</param>
/// <param name="TacitlyAccepted">Nombre réellement basculés (éligibilité confirmée sous verrou).</param>
internal readonly record struct TacitAcceptanceRunResult(int Evaluated, int TacitlyAccepted);
