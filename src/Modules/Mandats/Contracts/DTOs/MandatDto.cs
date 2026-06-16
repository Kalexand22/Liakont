namespace Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Vue de lecture d'un mandat (F15 §1.5/§2.2). DTO pur (aucune logique). <see cref="IsSelfBillingSuspended"/>
/// expose l'état calculé « 389 suspendu » (INV-MANDATS-4) sans dupliquer la règle côté consommateur.
/// </summary>
public sealed record MandatDto
{
    /// <summary>Identifiant technique du mandat.</summary>
    public required Guid Id { get; init; }

    /// <summary>Mandant auquel ce mandat se rapporte.</summary>
    public required Guid MandantId { get; init; }

    /// <summary>Référence métier du mandat (tenant-scopée).</summary>
    public required string Reference { get; init; }

    /// <summary>Texte de la clause de mandat.</summary>
    public required string ClauseText { get; init; }

    /// <summary>Mandat écrit (<c>true</c>) ou tacite (<c>false</c>) — F15 §1.5.</summary>
    public required bool EstEcrit { get; init; }

    /// <summary>Statut d'assujettissement déclaré (valeur opaque) ; <c>null</c> = suspendu.</summary>
    public string? AssujettissementStatus { get; init; }

    /// <summary>Délai de contestation (clause du mandat) ; <c>null</c> = bascule tacite impossible.</summary>
    public TimeSpan? ContestationDelay { get; init; }

    /// <summary>Identité du valideur ; <c>null</c> tant que non validé.</summary>
    public string? ValidatedBy { get; init; }

    /// <summary>Date de validation ; <c>null</c> tant que non validé.</summary>
    public DateOnly? ValidatedDate { get; init; }

    /// <summary>Date de révocation ; <c>null</c> tant que non révoqué.</summary>
    public DateTimeOffset? RevokedDate { get; init; }

    /// <summary>Le mandat a été validé humainement.</summary>
    public required bool IsValidated { get; init; }

    /// <summary>Le mandat a été révoqué.</summary>
    public required bool IsRevoked { get; init; }

    /// <summary>L'autofacturation 389 est suspendue (INV-MANDATS-4) — état calculé exposé pour la console et le pipeline.</summary>
    public required bool IsSelfBillingSuspended { get; init; }
}
