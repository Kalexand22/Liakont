namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Implémente la bascule tacite <c>PendingAcceptance → TacitlyAccepted</c> (MND04, ADR-0024 §4 / F15 §2.3),
/// pour le tenant courant. La condition fiscale « mandat écrit ET délai de contestation non null » est
/// <b>déjà encodée</b> dans <see cref="SelfBilledAcceptance.DeadlineUtc"/> (calculée à la création par
/// l'appelant qui lit le mandat — F15 §2.3 / INV-ACCEPT-3) : <c>DeadlineUtc != null</c> ⟺ bascule tacite
/// possible. Le job n'ajoute que la condition temporelle <c>now ≥ DeadlineUtc</c>. Sous mandat tacite ou
/// délai null, <c>DeadlineUtc</c> est null : aucun candidat, seule l'acceptation EXPRESSE débloque
/// (BOFiP §290, CLAUDE.md n°2/3 — jamais affaibli).
/// <para>
/// Robustesse : on SNAPSHOT les clés dues (lecteur), puis on traite chaque clé dans SA propre unité de
/// travail en rechargeant l'agrégat sous verrou (<c>FOR UPDATE</c>) et en RE-VÉRIFIANT l'éligibilité —
/// l'état a pu changer entre l'énumération et le verrou (acceptation expresse / contestation concurrente).
/// </para>
/// </summary>
internal sealed class TacitAcceptanceService : ITacitAcceptanceService
{
    /// <summary>Origine système tracée au journal (operator_id null = bascule par job, pas un opérateur humain).</summary>
    internal const string TacitOperatorName = "Bascule tacite (job)";

    private readonly ITacitAcceptanceCandidateReader _candidateReader;
    private readonly ISelfBilledAcceptanceUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public TacitAcceptanceService(
        ITacitAcceptanceCandidateReader candidateReader,
        ISelfBilledAcceptanceUnitOfWorkFactory unitOfWorkFactory,
        TimeProvider timeProvider)
    {
        _candidateReader = candidateReader;
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
    }

    public async Task<TacitAcceptanceRunResult> ProcessDueAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Snapshot des clés dues AVANT traitement (l'état traité se vide au fil des bascules : énumérer puis
        // re-paginer un état qui change est un faux-vert connu — voir lessons pipeline tenant-job).
        IReadOnlyList<TacitAcceptanceCandidate> candidates = await _candidateReader.ListDueAsync(now, ct);

        var tacitlyAccepted = 0;
        foreach (TacitAcceptanceCandidate candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            await using ISelfBilledAcceptanceUnitOfWork uow = await _unitOfWorkFactory.BeginAsync(ct);
            SelfBilledAcceptance? acceptance = await uow.GetForUpdateAsync(candidate.CompanyId, candidate.DocumentId, ct);

            if (acceptance is null || !IsDueForTacitAcceptance(acceptance, now))
            {
                // Plus éligible sous verrou (acceptation expresse / contestation entre-temps, ou échéance non
                // échue) : on ne bascule pas. La sortie du bloc abandonne la transaction → aucun effet.
                continue;
            }

            acceptance.AcceptTacitly();
            SelfBilledAcceptanceLogEntry entry = SelfBilledAcceptanceLogFactory.ForTransition(
                acceptance,
                fromState: SelfBilledAcceptanceState.PendingAcceptance,
                operatorId: null,
                operatorName: TacitOperatorName);

            await uow.SaveTransitionAsync(acceptance, entry, ct);
            await uow.CommitAsync(ct);
            tacitlyAccepted++;
        }

        return new TacitAcceptanceRunResult(candidates.Count, tacitlyAccepted);
    }

    /// <summary>
    /// Condition de bascule tacite (ADR-0024 §4 / INV-ACCEPT-3), re-vérifiée sous verrou : en attente,
    /// échéance renseignée (≡ mandat écrit ET délai non null) et échue. Mandat tacite ou délai null ⇒
    /// <c>DeadlineUtc</c> null ⇒ jamais éligible (seule l'acceptation expresse débloque).
    /// </summary>
    private static bool IsDueForTacitAcceptance(SelfBilledAcceptance acceptance, DateTimeOffset now)
        => acceptance.State == SelfBilledAcceptanceState.PendingAcceptance
            && acceptance.DeadlineUtc is { } deadline
            && deadline <= now;
}
