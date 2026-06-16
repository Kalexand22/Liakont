namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Projection du sous-graphe self-billing du module générique DocumentApproval (purpose
/// <c>SelfBilledAcceptance</c>, ADR-0028 §4) vers le vocabulaire fiscal <see cref="SelfBilledAcceptanceState"/>
/// (ADR-0024). Depuis SIG05, l'état et le journal vivent dans DocumentApproval ; cette correspondance garde
/// STABLE la surface <c>Contracts</c> du module Mandats (noms d'états exposés, <c>IsAccepted</c>) sans dupliquer
/// la machine. Le mapping se fait sur le <b>nom</b> d'état (les DTOs DocumentApproval exposent l'état par son
/// nom, jamais l'enum de domaine — la frontière Contracts n'expose pas <c>ValidationState</c>). La projection est
/// <b>restreinte</b> aux 4 états self-billing : tout autre nom (ValidationInProgress/Rejected/Expired) est HORS du
/// sous-graphe du purpose et ne doit jamais apparaître — il lève (garde anti-faux-vert, jamais une projection silencieuse).
/// </summary>
internal static class SelfBilledAcceptanceStateMap
{
    /// <summary>Le NOM d'état de validation (DocumentApproval) projeté en <see cref="SelfBilledAcceptanceState"/>.</summary>
    public static SelfBilledAcceptanceState FromValidationStateName(string validationStateName) => validationStateName switch
    {
        "PendingValidation" => SelfBilledAcceptanceState.PendingAcceptance,
        "Validated" => SelfBilledAcceptanceState.Accepted,
        "TacitlyValidated" => SelfBilledAcceptanceState.TacitlyAccepted,
        "Contested" => SelfBilledAcceptanceState.Contested,
        _ => throw new InvalidOperationException(
            $"État de validation « {validationStateName} » HORS du sous-graphe self-billing (ADR-0028 §4 : " +
            "PendingValidation/Validated/TacitlyValidated/Contested) : projection self-billing impossible."),
    };

    /// <summary>Nom d'état self-billing projeté (ou <c>null</c> si l'entrée source est <c>null</c>, ex. genèse du journal).</summary>
    public static string? NameOrNull(string? validationStateName)
        => validationStateName is null ? null : FromValidationStateName(validationStateName).ToString();

    /// <summary>Le gate d'émission est ouvert (état projeté Accepted/TacitlyAccepted, ADR-0024 §2).</summary>
    public static bool IsAccepted(SelfBilledAcceptanceState state)
        => state is SelfBilledAcceptanceState.Accepted or SelfBilledAcceptanceState.TacitlyAccepted;
}
