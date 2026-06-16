namespace Liakont.Modules.DocumentApproval.Domain;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// Politique d'un <see cref="ValidationPurpose"/> (ADR-0028 §4) : le <b>sous-graphe autorisé explicite</b>
/// (garde de purpose), l'autorisation de ré-essai, l'exigence de forme expresse (condition 3 du gate), l'usage
/// de slots N-parties, et le terminal négatif. Le graphe complet (7 états) est l'UNION des purposes ; aucun
/// purpose n'accède à toutes les arêtes. Une transition n'est autorisée que si elle existe dans le graphe
/// UNIVERSEL (<see cref="ValidationMachine"/>) <b>ET</b> que ses deux états appartiennent au sous-graphe.
/// </summary>
public sealed class ValidationPurposePolicy
{
    // Self-billing & avoir 261 : 4 états = machine ADR-0024 EXACTE (ValidationInProgress/Expired/Rejected
    // HORS sous-graphe ; Contested conservé, sens fiscal). Pas de ré-essai (Contested définitif).
    private static readonly IReadOnlySet<ValidationState> SelfBillingStates = new HashSet<ValidationState>
    {
        ValidationState.PendingValidation,
        ValidationState.Validated,
        ValidationState.TacitlyValidated,
        ValidationState.Contested,
    };

    // Signature expresse (mandat) : pas de bascule tacite (TacitlyValidated HORS sous-graphe), pas de Contested.
    private static readonly IReadOnlySet<ValidationState> ExpressSignatureStates = new HashSet<ValidationState>
    {
        ValidationState.PendingValidation,
        ValidationState.ValidationInProgress,
        ValidationState.Validated,
        ValidationState.Rejected,
        ValidationState.Expired,
    };

    // Signature avec bascule tacite possible (relevé chantier, N-parties) : graphe signature complet
    // (sans Contested, qui reste propre au self-billing).
    private static readonly IReadOnlySet<ValidationState> TacitCapableSignatureStates = new HashSet<ValidationState>
    {
        ValidationState.PendingValidation,
        ValidationState.ValidationInProgress,
        ValidationState.Validated,
        ValidationState.TacitlyValidated,
        ValidationState.Rejected,
        ValidationState.Expired,
    };

    private static readonly Dictionary<ValidationPurpose, ValidationPurposePolicy> Policies =
        new()
        {
            [ValidationPurpose.SelfBilledAcceptance] = new(
                ValidationPurpose.SelfBilledAcceptance,
                SelfBillingStates,
                allowsRetry: false,
                requiresExpressAcceptanceForm: true,
                usesSlots: false,
                negativeTerminal: ValidationState.Contested),

            // Défaut défendable (ADR-0028 §Points NON TRANCHÉS #9) : le 261 est self-billed → MÊME discipline
            // d'acceptation que le 389 (conservateur, aucune règle fiscale inventée). L'EXISTENCE du purpose
            // reste conditionnée à F15 §6.5 par le câblage du port (SIG06), pas par le mécanisme (SIG04).
            [ValidationPurpose.CreditNoteAcceptance] = new(
                ValidationPurpose.CreditNoteAcceptance,
                SelfBillingStates,
                allowsRetry: false,
                requiresExpressAcceptanceForm: true,
                usesSlots: false,
                negativeTerminal: ValidationState.Contested),

            [ValidationPurpose.MandateSignature] = new(
                ValidationPurpose.MandateSignature,
                ExpressSignatureStates,
                allowsRetry: true,
                requiresExpressAcceptanceForm: false,
                usesSlots: false,
                negativeTerminal: ValidationState.Rejected),

            [ValidationPurpose.ProgressStatementApproval] = new(
                ValidationPurpose.ProgressStatementApproval,
                TacitCapableSignatureStates,
                allowsRetry: true,
                requiresExpressAcceptanceForm: false,
                usesSlots: false,
                negativeTerminal: ValidationState.Rejected),

            [ValidationPurpose.MultiTierAccountingApproval] = new(
                ValidationPurpose.MultiTierAccountingApproval,
                TacitCapableSignatureStates,
                allowsRetry: true,
                requiresExpressAcceptanceForm: false,
                usesSlots: true,
                negativeTerminal: ValidationState.Rejected),

            [ValidationPurpose.MultiPartySignature] = new(
                ValidationPurpose.MultiPartySignature,
                TacitCapableSignatureStates,
                allowsRetry: true,
                requiresExpressAcceptanceForm: false,
                usesSlots: true,
                negativeTerminal: ValidationState.Rejected),
        };

    private ValidationPurposePolicy(
        ValidationPurpose purpose,
        IReadOnlySet<ValidationState> allowedStates,
        bool allowsRetry,
        bool requiresExpressAcceptanceForm,
        bool usesSlots,
        ValidationState negativeTerminal)
    {
        Purpose = purpose;
        AllowedStates = allowedStates;
        AllowsRetry = allowsRetry;
        RequiresExpressAcceptanceForm = requiresExpressAcceptanceForm;
        UsesSlots = usesSlots;
        NegativeTerminal = negativeTerminal;
    }

    /// <summary>Le purpose gouverné.</summary>
    public ValidationPurpose Purpose { get; }

    /// <summary>Les états du sous-graphe autorisé (toute transition vers un état HORS de cet ensemble est rejetée).</summary>
    public IReadOnlySet<ValidationState> AllowedStates { get; }

    /// <summary>Le ré-essai par nouvel <c>attempt</c> est autorisé (purposes signature) ou EXCLU (self-billing : Contested définitif).</summary>
    public bool AllowsRetry { get; }

    /// <summary>La condition 3 du gate (acceptation expresse explicite sur transition <c>Validated</c>) s'applique à ce purpose.</summary>
    public bool RequiresExpressAcceptanceForm { get; }

    /// <summary>Le purpose agrège N parties par SLOTS (ADR-0028 §8).</summary>
    public bool UsesSlots { get; }

    /// <summary>Terminal négatif du purpose : <c>Rejected</c> (signature) ou <c>Contested</c> (sens fiscal self-billing).</summary>
    public ValidationState NegativeTerminal { get; }

    /// <summary>Politique d'un purpose (toujours définie — le dictionnaire couvre tous les membres de l'enum).</summary>
    public static ValidationPurposePolicy For(ValidationPurpose purpose) => Policies[purpose];

    /// <summary>Toutes les politiques (base des tests cartésiens « garde de purpose »).</summary>
    public static IReadOnlyCollection<ValidationPurposePolicy> All() => Policies.Values;

    /// <summary><c>true</c> si <paramref name="state"/> appartient au sous-graphe autorisé du purpose.</summary>
    public bool IsStateAllowed(ValidationState state) => AllowedStates.Contains(state);

    /// <summary>
    /// La transition <paramref name="from"/> → <paramref name="to"/> est autorisée pour ce purpose ssi elle
    /// existe dans le graphe universel ET que ses deux extrémités sont dans le sous-graphe autorisé.
    /// </summary>
    public bool AllowsTransition(ValidationState from, ValidationState to)
        => ValidationMachine.AllowsUniversalTransition(from, to)
           && AllowedStates.Contains(from)
           && AllowedStates.Contains(to);
}
