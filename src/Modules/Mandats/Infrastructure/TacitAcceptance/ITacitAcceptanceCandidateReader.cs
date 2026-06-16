namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

/// <summary>
/// Énumère les acceptations candidates à la bascule tacite (MND04) dans la base DU tenant courant
/// (database-per-tenant : la lecture porte intrinsèquement sur les données de ce tenant). Une acceptation
/// est candidate quand elle est en attente ET porte une échéance échue : <c>state = PendingAcceptance</c>,
/// <c>deadline_utc</c> NON NULL (≡ mandat écrit ET délai de contestation renseigné — calculé à la création,
/// F15 §2.3 / ADR-0024 §4) et <c>deadline_utc ≤ now</c>. La condition finale (re-vérifiée sous verrou par le
/// service) reste « now ≥ DeadlineUtc » : ce lecteur ne fait que pré-filtrer le balayage.
/// </summary>
internal interface ITacitAcceptanceCandidateReader
{
    /// <summary>
    /// Liste les clés <c>(company_id, document_id)</c> des acceptations en attente dont l'échéance de bascule
    /// tacite est échue à <paramref name="nowUtc"/>, dans la base du tenant courant. Read-only, hors transaction.
    /// </summary>
    Task<IReadOnlyList<TacitAcceptanceCandidate>> ListDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

/// <summary>Clé d'une acceptation candidate à la bascule tacite, scopée par <c>company_id</c> (INV-MANDATS-1).</summary>
/// <param name="CompanyId">Tenant propriétaire (porté par la ligne ; utilisé pour le verrou ciblé).</param>
/// <param name="DocumentId">Document self-billed concerné.</param>
internal readonly record struct TacitAcceptanceCandidate(Guid CompanyId, Guid DocumentId);
