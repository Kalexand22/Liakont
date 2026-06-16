namespace Liakont.Modules.DocumentApproval.Domain;

using Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// Graphe d'arêtes UNIVERSEL de la machine de validation (union de tous les purposes, ADR-0028 §3). La garde
/// de purpose (<see cref="ValidationPurposePolicy"/>) restreint ensuite ce graphe à un sous-graphe autorisé.
/// <para>
/// <see cref="ValidationState.PendingValidation"/> a des arêtes DIRECTES vers tous les terminaux (chemin
/// <c>Recorded</c>/synchrone) ET vers <see cref="ValidationState.ValidationInProgress"/> (intermédiaire
/// optionnel). <see cref="ValidationState.ValidationInProgress"/> → terminaux SAUF <c>Contested</c>
/// (la contestation fiscale ne se fait que depuis <c>PendingValidation</c>, self-billing). Aucune arête depuis
/// un terminal (aucun retour arrière, INV-APPROVAL-2).
/// </para>
/// </summary>
internal static class ValidationMachine
{
    private static readonly Dictionary<ValidationState, IReadOnlySet<ValidationState>> Edges =
        new()
        {
            [ValidationState.PendingValidation] = new HashSet<ValidationState>
            {
                ValidationState.ValidationInProgress,
                ValidationState.Validated,
                ValidationState.TacitlyValidated,
                ValidationState.Rejected,
                ValidationState.Contested,
                ValidationState.Expired,
            },
            [ValidationState.ValidationInProgress] = new HashSet<ValidationState>
            {
                ValidationState.Validated,
                ValidationState.TacitlyValidated,
                ValidationState.Rejected,
                ValidationState.Expired,
            },
        };

    /// <summary>Un état est terminal ssi il n'a AUCUNE arête sortante (Validated/TacitlyValidated/Rejected/Contested/Expired).</summary>
    public static bool IsTerminal(ValidationState state) => !Edges.ContainsKey(state);

    /// <summary>La transition <paramref name="from"/> → <paramref name="to"/> existe dans le graphe universel (avant garde de purpose).</summary>
    public static bool AllowsUniversalTransition(ValidationState from, ValidationState to)
        => Edges.TryGetValue(from, out var targets) && targets.Contains(to);
}
