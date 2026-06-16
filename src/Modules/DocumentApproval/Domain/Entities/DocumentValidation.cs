namespace Liakont.Modules.DocumentApproval.Domain.Entities;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Agrégat générique de validation d'un document (ADR-0028 §2/§3, F17 §3/§4). CŒUR réutilisable du lot
/// signature : une même mécanique sert plusieurs <see cref="ValidationPurpose"/> (acceptation 389/261,
/// signature de mandat, relevé de chantier, circuit multi-paliers, co-signature N parties).
/// <para>
/// Clé métier <c>(company_id, document_id, validation_purpose, attempt)</c>, tenant-scopée (CLAUDE.md n°9). État
/// <b>mutable</b> (écrasé à chaque transition) ; la traçabilité vient du journal append-only
/// <c>document_approval_log</c> (INV-APPROVAL-6 : chaque transition écrit une ligne dans la MÊME transaction —
/// assuré par <c>IDocumentValidationUnitOfWork</c>). La machine est <b>FERMÉE</b>
/// (<see cref="ValidationState"/>), restreinte par <see cref="ValidationPurposePolicy"/> au sous-graphe du
/// purpose (INV-APPROVAL-2/3 ; aucun retour arrière depuis un terminal).
/// </para>
/// <para>
/// SIG04 livre le mécanisme ; la valeur du niveau eIDAS requis et la modalité d'acceptation expresse sont du
/// PARAMÉTRAGE TENANT câblé par les ports de purpose (SIG06). Aucune règle fiscale/juridique inventée
/// (CLAUDE.md n°2).
/// </para>
/// </summary>
public sealed class DocumentValidation
{
    private readonly List<ApprovalSlot> _slots = [];

    private DocumentValidation()
    {
    }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-APPROVAL-6).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Document concerné (référence lâche — aucun couplage de schéma cross-module).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Finalité de validation (déclare le sous-graphe autorisé).</summary>
    public ValidationPurpose Purpose { get; private set; }

    /// <summary>Numéro de tentative (≥ 1). Pour les purposes sans ré-essai (self-billing), reste 1.</summary>
    public int Attempt { get; private set; }

    /// <summary>État courant (machine fermée <see cref="ValidationState"/>, INV-APPROVAL-2).</summary>
    public ValidationState State { get; private set; }

    /// <summary>Niveau de la preuve attachée (agrégat mono-partie). <c>None</c> tant que non validé ; le N-parties porte le niveau PAR slot.</summary>
    public SignatureLevel ProofLevel { get; private set; }

    /// <summary>Une acceptation expresse explicite a été enregistrée (condition 3 du gate self-billing, ADR-0028 §5).</summary>
    public bool ExpressAcceptanceRecorded { get; private set; }

    /// <summary>Échéance (UTC) de bascule tacite / timeout ; <c>null</c> = bascule tacite impossible.</summary>
    public DateTimeOffset? DeadlineUtc { get; private set; }

    /// <summary>Date de création (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière transition (UTC) ; <c>null</c> tant qu'aucune transition n'a eu lieu.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Slots d'approbation N-parties (vide pour un purpose mono-partie), dans l'ordre de création.</summary>
    public IReadOnlyList<ApprovalSlot> Slots => _slots;

    /// <summary>L'état est terminal (aucune transition possible — INV-APPROVAL-2).</summary>
    public bool IsTerminal => ValidationMachine.IsTerminal(State);

    /// <summary>
    /// Crée une tentative à l'état initial <see cref="ValidationState.PendingValidation"/>. Pour un purpose
    /// N-parties (<see cref="ValidationPurposePolicy.UsesSlots"/>), <paramref name="signerIds"/> est obligatoire
    /// et définit l'ensemble FIXE de slots distincts ; pour un purpose mono-partie, il doit être vide/null.
    /// </summary>
    public static DocumentValidation Create(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset? deadlineUtc,
        int attempt = 1,
        IEnumerable<string>? signerIds = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (company_id) est obligatoire.", nameof(companyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Le document concerné (document_id) est obligatoire.", nameof(documentId));
        }

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Le numéro de tentative doit être ≥ 1.");
        }

        var policy = ValidationPurposePolicy.For(purpose);
        if (attempt > 1 && !policy.AllowsRetry)
        {
            throw new InvalidOperationException(
                $"Le purpose « {purpose} » est EXCLU du ré-essai (ADR-0028 §6) : l'attempt doit rester 1 " +
                "(correction = document compensatoire + nouveau document_id, jamais une nouvelle tentative).");
        }

        var signerList = signerIds?.ToList() ?? [];
        var slots = new List<ApprovalSlot>();
        if (policy.UsesSlots)
        {
            if (signerList.Count == 0)
            {
                throw new ArgumentException(
                    $"Le purpose N-parties « {purpose} » exige au moins un signataire (slot).", nameof(signerIds));
            }

            if (signerList.Distinct(StringComparer.Ordinal).Count() != signerList.Count)
            {
                throw new ArgumentException(
                    "Les identifiants de signataires (slots) doivent être distincts.", nameof(signerIds));
            }

            slots.AddRange(signerList.Select(ApprovalSlot.CreatePending));
        }
        else if (signerList.Count > 0)
        {
            throw new ArgumentException(
                $"Le purpose mono-partie « {purpose} » n'accepte pas de slots de signataires.", nameof(signerIds));
        }

        var validation = new DocumentValidation
        {
            CompanyId = companyId,
            DocumentId = documentId,
            Purpose = purpose,
            Attempt = attempt,
            State = ValidationState.PendingValidation,
            ProofLevel = SignatureLevel.None,
            ExpressAcceptanceRecorded = false,
            DeadlineUtc = deadlineUtc,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
        validation._slots.AddRange(slots);
        return validation;
    }

    /// <summary>Reconstitue l'agrégat depuis la base (chemin de chargement) — sans rejouer la machine.</summary>
    public static DocumentValidation Reconstitute(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        int attempt,
        ValidationState state,
        SignatureLevel proofLevel,
        bool expressAcceptanceRecorded,
        DateTimeOffset? deadlineUtc,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        IEnumerable<ApprovalSlot>? slots = null)
    {
        var validation = new DocumentValidation
        {
            CompanyId = companyId,
            DocumentId = documentId,
            Purpose = purpose,
            Attempt = attempt,
            State = state,
            ProofLevel = proofLevel,
            ExpressAcceptanceRecorded = expressAcceptanceRecorded,
            DeadlineUtc = deadlineUtc,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
        if (slots is not null)
        {
            validation._slots.AddRange(slots);
        }

        return validation;
    }

    /// <summary>Passe une demande de signature EN COURS (purposes signature asynchrones) : <c>Pending</c> → <c>ValidationInProgress</c>.</summary>
    public void MarkInProgress() => TransitionTo(ValidationState.ValidationInProgress);

    /// <summary>
    /// Validation EXPRESSE mono-partie (acceptation enregistrée ou preuve rattachée) : → <see cref="ValidationState.Validated"/>.
    /// <paramref name="proofLevel"/> est le niveau de la preuve (Recorded pour une acceptation enregistrée, SES/AES/QES
    /// pour une signature). <paramref name="expressAcceptanceRecorded"/> trace l'acceptation explicite (condition 3 du gate).
    /// </summary>
    public void Validate(SignatureLevel proofLevel, bool expressAcceptanceRecorded = true)
    {
        EnsureSingleParty();
        SignatureLevelAssurance.EnsureSingleLevel(proofLevel, nameof(proofLevel));
        if (proofLevel == SignatureLevel.None)
        {
            throw new ArgumentException(
                "Une validation expresse doit porter un niveau de preuve (Recorded/SES/AES/QES).", nameof(proofLevel));
        }

        TransitionTo(ValidationState.Validated);
        ProofLevel = proofLevel;
        ExpressAcceptanceRecorded = expressAcceptanceRecorded;
    }

    /// <summary>
    /// Bascule TACITE mono-partie (job au-delà de l'échéance, si la politique du purpose l'autorise) :
    /// → <see cref="ValidationState.TacitlyValidated"/>. La preuve effective est <c>Recorded</c> (sans preuve
    /// rattachée — ne satisfait que Recorded au gate). Les conditions « délai écoulé / mandat écrit » sont la
    /// responsabilité du job appelant (SIG06), pas de l'agrégat.
    /// </summary>
    public void MarkTacitlyValidated()
    {
        EnsureSingleParty();
        TransitionTo(ValidationState.TacitlyValidated);
        ProofLevel = SignatureLevel.Recorded;
        ExpressAcceptanceRecorded = false;
    }

    /// <summary>Refus d'une demande de signature mono-partie : → <see cref="ValidationState.Rejected"/> (terminal).</summary>
    public void Reject()
    {
        EnsureSingleParty();
        TransitionTo(ValidationState.Rejected);
    }

    /// <summary>Contestation d'un self-billing mono-partie dans le délai : → <see cref="ValidationState.Contested"/> (terminal, sens fiscal).</summary>
    public void Contest()
    {
        EnsureSingleParty();
        TransitionTo(ValidationState.Contested);
    }

    /// <summary>Expiration (délai dépassé sans complétion) : → <see cref="ValidationState.Expired"/> (terminal ; purposes signature / timeout N-parties).</summary>
    public void Expire() => TransitionTo(ValidationState.Expired);

    /// <summary>
    /// Approuve le slot d'un signataire (N-parties) avec son niveau de preuve. <b>Idempotent par SignerId</b> :
    /// une 2ᵉ approbation du même signataire est sans effet. Quand TOUS les slots distincts sont approuvés,
    /// l'agrégat bascule en <see cref="ValidationState.Validated"/> (complétude = slots distincts, jamais un compteur).
    /// </summary>
    public void ApproveSlot(string signerId, SignatureLevel proofLevel, string? proofId = null)
    {
        EnsureSlotsPurpose();
        var slot = FindSlot(signerId);
        if (slot.IsApproved)
        {
            // Idempotence (ADR-0028 §8) : une 2ᵉ preuve du même signataire ne remplit rien de plus.
            return;
        }

        EnsureNotTerminal();
        if (slot.State == ApprovalSlotState.Rejected)
        {
            throw new InvalidOperationException(
                $"Le slot « {signerId} » a été refusé : il ne peut plus être approuvé (terminaison négative, §8).");
        }

        SignatureLevelAssurance.EnsureSingleLevel(proofLevel, nameof(proofLevel));
        if (proofLevel == SignatureLevel.None)
        {
            throw new ArgumentException(
                "Une approbation de slot doit porter un niveau de preuve (Recorded/SES/AES/QES).", nameof(proofLevel));
        }

        slot.Approve(proofLevel, proofId);
        UpdatedAt = DateTimeOffset.UtcNow;

        if (_slots.All(s => s.IsApproved))
        {
            // Complétude N-parties : transition vers Validated (les niveaux par slot sont conservés pour la
            // Règle de gate §5 cond. 2). Approbations N-parties = gestes express tracés.
            TransitionTo(ValidationState.Validated);
            ExpressAcceptanceRecorded = true;
        }
    }

    /// <summary>
    /// Refuse le slot d'un signataire (N-parties) : bascule l'agrégat en terminal négatif IMMÉDIATEMENT
    /// (terminaison négative, ADR-0028 §8) — un document co-signé dont une partie refuse ne peut jamais compléter.
    /// </summary>
    public void RejectSlot(string signerId)
    {
        EnsureSlotsPurpose();
        var slot = FindSlot(signerId);
        if (slot.State == ApprovalSlotState.Rejected)
        {
            // Idempotence : un refus déjà acté ne re-déclenche pas une transition.
            return;
        }

        EnsureNotTerminal();
        slot.Reject();
        UpdatedAt = DateTimeOffset.UtcNow;
        TransitionTo(ValidationPurposePolicy.For(Purpose).NegativeTerminal);
    }

    private void TransitionTo(ValidationState target)
    {
        var policy = ValidationPurposePolicy.For(Purpose);
        if (!policy.AllowsTransition(State, target))
        {
            throw new InvalidOperationException(
                $"Transition de validation refusée pour le purpose « {Purpose} » : « {State} » → « {target} » " +
                "n'est pas autorisée (machine fermée + sous-graphe de purpose — INV-APPROVAL-2/3 ; aucun retour " +
                "arrière depuis un terminal). Action opérateur : une validation terminale se corrige par un " +
                "document compensatoire ou une nouvelle tentative (purposes signature), jamais par réouverture.");
        }

        State = target;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureSingleParty()
    {
        if (ValidationPurposePolicy.For(Purpose).UsesSlots)
        {
            throw new InvalidOperationException(
                $"Le purpose N-parties « {Purpose} » se valide par ses SLOTS (ApproveSlot/RejectSlot), " +
                "pas par une transition mono-partie.");
        }
    }

    private void EnsureSlotsPurpose()
    {
        if (!ValidationPurposePolicy.For(Purpose).UsesSlots)
        {
            throw new InvalidOperationException(
                $"Le purpose mono-partie « {Purpose} » n'a pas de slots (ApproveSlot/RejectSlot interdits).");
        }
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"L'agrégat est dans un état terminal « {State} » : aucune mutation de slot n'est possible " +
                "(machine fermée, aucun retour arrière — INV-APPROVAL-2).");
        }
    }

    private ApprovalSlot FindSlot(string signerId)
    {
        if (string.IsNullOrWhiteSpace(signerId))
        {
            throw new ArgumentException("L'identifiant du signataire (SignerId) est obligatoire.", nameof(signerId));
        }

        return _slots.FirstOrDefault(s => string.Equals(s.SignerId, signerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Le signataire « {signerId} » ne fait pas partie des slots définis à la création de cette tentative.");
    }
}
