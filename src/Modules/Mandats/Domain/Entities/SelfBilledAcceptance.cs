namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Agrégat d'acceptation d'une auto-facture sous mandat (type 389, art. 289 I-2 CGI — ADR-0024, F15 §2.2/§2.3).
/// L'acceptation est un cycle <b>orthogonal</b> à l'émission : on <b>n'étend pas</b> la machine
/// <c>DocumentState</c> du module <c>Documents</c> (INV-ACCEPT-1) — le blocage de l'émission réutilise l'état
/// <c>Blocked</c> existant via un motif (port <c>ISelfBilledGate</c>, livré par MND03). Clé métier
/// <c>(company_id, document_id)</c>, tenant-scopée (CLAUDE.md n°9, INV-MANDATS-1).
/// <para>
/// L'état est <b>mutable</b> (écrasé à chaque transition) ; la traçabilité vient du journal append-only
/// <c>self_billed_acceptance_log</c> (INV-ACCEPT-5 : chaque transition écrit une ligne dans la MÊME
/// transaction — assuré par <c>ISelfBilledAcceptanceUnitOfWork</c>). La machine est <b>fermée</b>
/// (<see cref="SelfBilledAcceptanceState"/>, INV-ACCEPT-4).
/// </para>
/// <para>
/// MND02 ne tranche <b>aucun point fiscal</b> : la <b>valeur</b> du délai de contestation (porté ici par
/// <see cref="DeadlineUtc"/>, calculé par l'appelant) relève du contrat de mandat (F15 §6.4) ; l'avoir de
/// correction d'un <see cref="SelfBilledAcceptanceState.Contested"/> (BT-3 = 261) est NON TRANCHÉ (F15 §6.5)
/// et n'est PAS implémenté ici (ADR-0024 §5). L'allocation du <see cref="AllocatedNumber"/> (BT-1 fiscal par
/// mandant) est livrée par MND05 (ADR-0025) — <c>null</c> tant qu'il n'est pas alloué.
/// </para>
/// </summary>
public sealed class SelfBilledAcceptance
{
    private SelfBilledAcceptance()
    {
    }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-MANDATS-1).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Document self-billed concerné (identifiant du module <c>Documents</c>, référence lâche — aucun couplage de schéma).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>État courant de l'acceptation (machine fermée <see cref="SelfBilledAcceptanceState"/>, INV-ACCEPT-4).</summary>
    public SelfBilledAcceptanceState State { get; private set; }

    /// <summary>
    /// BT-1 fiscal alloué par mandant (F15 §3, ADR-0025). <c>null</c> tant qu'il n'est pas alloué — l'allocation
    /// <c>get-or-create</c> est livrée par MND05 ; MND02 ne fait que porter le champ (jamais inventé ici).
    /// </summary>
    public string? AllocatedNumber { get; private set; }

    /// <summary>Instant (UTC) où le document est entré en attente d'acceptation (base de calcul de <see cref="DeadlineUtc"/>).</summary>
    public DateTimeOffset PendingSince { get; private set; }

    /// <summary>
    /// Échéance (UTC) de bascule tacite = <see cref="PendingSince"/> + délai de contestation du mandat. <c>null</c>
    /// = bascule tacite impossible (mandat tacite ou délai non renseigné — F15 §2.3, INV-ACCEPT-3). La valeur est
    /// <b>calculée par l'appelant</b> (qui lit le mandat, hors MND02) ; l'agrégat ne fait que la mémoriser.
    /// </summary>
    public DateTimeOffset? DeadlineUtc { get; private set; }

    /// <summary>Date de création de l'enregistrement (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière transition (UTC) ; <c>null</c> tant que l'agrégat n'a pas transité.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// L'acceptation <b>ouvre le gate</b> d'émission : l'état est <see cref="SelfBilledAcceptanceState.Accepted"/>
    /// ou <see cref="SelfBilledAcceptanceState.TacitlyAccepted"/> (ADR-0024 §2). La <b>garde</b> qui bloque
    /// réellement l'émission (port <c>ISelfBilledGate</c>) est livrée par MND03 ; MND02 n'expose que l'état calculé.
    /// </summary>
    public bool IsAccepted =>
        State is SelfBilledAcceptanceState.Accepted or SelfBilledAcceptanceState.TacitlyAccepted;

    /// <summary>L'état est terminal (tout sauf <see cref="SelfBilledAcceptanceState.PendingAcceptance"/>) : aucune transition possible.</summary>
    public bool IsTerminal => State != SelfBilledAcceptanceState.PendingAcceptance;

    /// <summary>
    /// Crée une acceptation à l'état initial <see cref="SelfBilledAcceptanceState.PendingAcceptance"/> (l'émission
    /// est bloquée tant qu'on n'a pas accepté). <paramref name="deadlineUtc"/> est facultatif (<c>null</c> = bascule
    /// tacite impossible) ; s'il est renseigné, il ne peut pas précéder <paramref name="pendingSince"/>.
    /// </summary>
    public static SelfBilledAcceptance Create(
        Guid companyId,
        Guid documentId,
        DateTimeOffset pendingSince,
        DateTimeOffset? deadlineUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (company_id) est obligatoire.", nameof(companyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Le document concerné (document_id) est obligatoire.", nameof(documentId));
        }

        if (deadlineUtc is not null && deadlineUtc.Value < pendingSince)
        {
            throw new ArgumentException(
                "L'échéance de bascule tacite ne peut pas précéder l'entrée en attente (deadline_utc < pending_since).",
                nameof(deadlineUtc));
        }

        return new SelfBilledAcceptance
        {
            CompanyId = companyId,
            DocumentId = documentId,
            State = SelfBilledAcceptanceState.PendingAcceptance,
            AllocatedNumber = null,
            PendingSince = pendingSince,
            DeadlineUtc = deadlineUtc,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    /// <summary>Reconstitue l'agrégat depuis la base (chemin de chargement) — sans rejouer la machine d'état.</summary>
    public static SelfBilledAcceptance Reconstitute(
        Guid companyId,
        Guid documentId,
        SelfBilledAcceptanceState state,
        string? allocatedNumber,
        DateTimeOffset pendingSince,
        DateTimeOffset? deadlineUtc,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new SelfBilledAcceptance
        {
            CompanyId = companyId,
            DocumentId = documentId,
            State = state,
            AllocatedNumber = allocatedNumber,
            PendingSince = pendingSince,
            DeadlineUtc = deadlineUtc,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Acceptation <b>expresse</b> (opérateur/mandant) : <see cref="SelfBilledAcceptanceState.PendingAcceptance"/>
    /// → <see cref="SelfBilledAcceptanceState.Accepted"/>. Refusée depuis un état terminal (machine fermée).
    /// </summary>
    public void AcceptExpressly() => TransitionTo(SelfBilledAcceptanceState.Accepted);

    /// <summary>
    /// Acceptation <b>tacite</b> par non-contestation (job MND04, mandat écrit uniquement) :
    /// <see cref="SelfBilledAcceptanceState.PendingAcceptance"/> → <see cref="SelfBilledAcceptanceState.TacitlyAccepted"/>.
    /// L'agrégat enforce uniquement la <b>fermeture</b> de la machine ; les conditions « mandat écrit ET délai
    /// non null ET now ≥ DeadlineUtc » sont la responsabilité du job (ADR-0024 §4, INV-ACCEPT-3, MND04).
    /// </summary>
    public void AcceptTacitly() => TransitionTo(SelfBilledAcceptanceState.TacitlyAccepted);

    /// <summary>
    /// Contestation dans le délai : <see cref="SelfBilledAcceptanceState.PendingAcceptance"/> →
    /// <see cref="SelfBilledAcceptanceState.Contested"/>. La correction se fait ensuite par avoir + nouvelle
    /// facture (F15 §2.3, NON TRANCHÉ §6.5 — hors MND02), jamais par un retour arrière d'état.
    /// </summary>
    public void Contest() => TransitionTo(SelfBilledAcceptanceState.Contested);

    private void TransitionTo(SelfBilledAcceptanceState target)
    {
        if (State != SelfBilledAcceptanceState.PendingAcceptance)
        {
            throw new InvalidOperationException(
                $"Transition d'acceptation refusée : l'état « {State} » est terminal (machine fermée " +
                "PendingAcceptance → {Accepted, TacitlyAccepted, Contested}, aucun retour arrière — INV-ACCEPT-4). " +
                "Action opérateur : une auto-facture contestée se corrige par avoir + nouvelle facture, jamais par réouverture.");
        }

        State = target;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
