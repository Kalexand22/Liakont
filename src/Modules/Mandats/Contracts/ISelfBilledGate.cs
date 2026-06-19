namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Garde d'émission des auto-factures sous mandat (port d'INVERSION DE DÉPENDANCE — ADR-0024 §3,
/// INV-ACCEPT-2, F15 §2.3). Le pipeline l'interroge AVANT de rendre un document self-billed émissible :
/// l'émission n'est autorisée que si le workflow d'acceptation (<c>SelfBilledAcceptance</c>, MND02) est
/// ouvert (état <c>Accepted</c> ou <c>TacitlyAccepted</c>). Tout autre cas — en attente, contestée, ou AUCUN
/// enregistrement d'acceptation — bloque l'émission : « bloquer plutôt qu'émettre faux » (CLAUDE.md n°3).
/// </summary>
/// <remarks>
/// Frontière (CLAUDE.md n°6/14, INV-MANDATS-2) : Documents/Pipeline ne dépendent QUE de cette abstraction,
/// JAMAIS du module Mandats concret — le pipeline reste testable avec un gate factice. La lecture est scopée
/// par tenant (<paramref name="companyId"/> résolu par l'appelant via <c>ICompanyFilter</c>, jamais fourni
/// par le client) ; aucune lecture cross-tenant (CLAUDE.md n°9/17, INV-MANDATS-1).
/// </remarks>
public interface ISelfBilledGate
{
    /// <summary>
    /// Statue sur l'émissibilité d'un document self-billed au regard de son acceptation. Le résultat est une
    /// LECTURE pure (aucune mutation) : autorisé uniquement si l'acceptation est <c>Accepted</c> ou
    /// <c>TacitlyAccepted</c> ; sinon (y compris acceptation absente) émission non autorisée.
    /// </summary>
    Task<SelfBilledGateDecision> EvaluateEmissionAsync(Guid companyId, Guid documentId, CancellationToken ct = default);
}
