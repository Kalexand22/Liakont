namespace Liakont.Modules.Pipeline.Infrastructure.Rectification;

using Liakont.Modules.Pipeline.Domain.Rectification;

/// <summary>
/// Résultat d'une tentative de rectification d'une période (PIP04, flux RE). Porte la
/// <see cref="Decision"/> prise, le <see cref="Rectification"/> reconstruit (agrégat complet de la période) et
/// un <see cref="Detail"/> opérateur (français). <see cref="Transmitted"/> est vrai uniquement quand un
/// rectificatif a été ACCEPTÉ par la Plateforme Agréée.
/// </summary>
public sealed record ReportRectificationOutcome
{
    /// <summary>Décision effectivement prise pour la période.</summary>
    public required ReportRectificationDecision Decision { get; init; }

    /// <summary>Agrégat rectifié complet reconstruit (annule-et-remplace), ou <c>null</c> si rien à reconstruire.</summary>
    public ReportRectification? Rectification { get; init; }

    /// <summary>Message opérateur (français) décrivant l'issue, ou <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Vrai si un rectificatif a été transmis ET accepté par la Plateforme Agréée.</summary>
    public bool Transmitted => Decision == ReportRectificationDecision.Transmitted;
}
