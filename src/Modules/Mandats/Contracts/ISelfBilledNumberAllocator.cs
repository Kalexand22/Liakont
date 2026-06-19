namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Allocateur du numéro fiscal BT-1 de l'autofacturation 389 (ADR-0025, F15 §3 — MND05). L'HYBRIDE :
/// la clé d'idempotence interne (numéro source, déjà au pivot et déjà hashé) ≠ le BT-1 fiscal (alloué par
/// mandant). Le pipeline l'interroge AU PLUS TARD, juste avant l'envoi, APRÈS l'acceptation (le gate
/// <see cref="ISelfBilledGate"/> a ouvert l'émission) : seuls les documents réellement émis consomment un
/// numéro (minimise les trous — F15 §3.2 / ADR-0025 §5).
/// </summary>
/// <remarks>
/// Frontière (CLAUDE.md n°6/14, INV-MANDATS-2) : Pipeline/Documents ne dépendent QUE de cette abstraction,
/// JAMAIS du module Mandats concret. L'allocation est <b>idempotente</b> sur la clé source (INV-BT1-2) et prend
/// un <b>verrou de séquence par mandant</b> (INV-BT1-4) ; le BT-1 alloué est une valeur SÉPARÉE, assignée HORS
/// du payload hashé (INV-BT1-1, ADR-0007 préservé) — il n'entre jamais dans <c>CanonicalJson</c>. La résolution
/// du mandant à partir du document (vendeur fiscal = mandant en 389) est portée par l'appelant ; tout est scopé
/// par <paramref name="companyId"/> (résolu via <c>ICompanyFilter</c>, jamais cross-tenant — CLAUDE.md n°9/17).
/// </remarks>
public interface ISelfBilledNumberAllocator
{
    /// <summary>
    /// Alloue (ou relit) le BT-1 fiscal du document self-billed et l'assigne à son acceptation
    /// (<c>self_billed_acceptances.allocated_number</c>, hors payload hashé), le tout dans une transaction unique.
    /// <para>
    /// GET-OR-CREATE sur <paramref name="sourceReference"/> : un même document source relit TOUJOURS le même
    /// numéro (INV-BT1-2), sans ré-allouer ni avancer la séquence. La séquence du mandant est verrouillée le
    /// temps de l'allocation (chronologie/continuité par mandant, INV-BT1-4).
    /// </para>
    /// <para>
    /// Fail-closed (CLAUDE.md n°3) : <paramref name="sourceReference"/> vide → rejet (un 389 sans clé source n'est
    /// pas numérotable — substitution d'invariant, INV-BT1-3) ; mandant inconnu pour ce tenant → rejet (préfixe
    /// non sourçable, on ne devine pas) ; acceptation absente pour <paramref name="documentId"/> → rejet (un
    /// document self-billed atteint l'allocation APRÈS acceptation).
    /// </para>
    /// </summary>
    /// <returns>Le BT-1 fiscal formaté (préfixe du mandant + valeur de séquence).</returns>
    Task<string> AllocateAsync(
        Guid companyId,
        Guid mandantId,
        Guid documentId,
        string sourceReference,
        CancellationToken ct = default);
}
