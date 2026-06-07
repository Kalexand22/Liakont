namespace Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Encaissement écarté de l'agrégation (avec son motif opérateur), pour la trace d'exécution et les alertes.
/// Un écart n'est jamais une perte : il rend visible pourquoi l'encaissement n'est pas agrégé (CLAUDE.md n°12).
/// </summary>
public sealed record PaymentExclusion
{
    /// <summary>Numéro du bordereau concerné, ou <c>null</c> si l'encaissement n'en porte pas.</summary>
    public string? RelatedDocumentNumber { get; init; }

    /// <summary>Raison de l'exclusion.</summary>
    public required PaymentExclusionReason Reason { get; init; }

    /// <summary>Message opérateur (français, action corrective — CLAUDE.md n°12).</summary>
    public required string Detail { get; init; }
}
