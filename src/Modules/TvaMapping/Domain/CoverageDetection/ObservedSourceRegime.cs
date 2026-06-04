namespace Liakont.Modules.TvaMapping.Domain.CoverageDetection;

/// <summary>
/// Régime de TVA source OBSERVÉ pour un tenant (métadonnées de push de l'agent, persistées par
/// tenant — PIV04), tel que consommé par la détection de couverture (item TVA03, F03 §4.3). Valeur
/// BRUTE : aucune interprétation fiscale, aucune règle inventée (CLAUDE.md n°2). Sert aussi d'entrée
/// classée dans le rapport (<see cref="MappingCoverageReport.CoveredRegimes"/> /
/// <see cref="MappingCoverageReport.AbsentRegimes"/>).
/// </summary>
public sealed record ObservedSourceRegime
{
    /// <summary>Code du régime dans le système source (brut, comparé EXACTEMENT à la table — INV-012).</summary>
    public required string Code { get; init; }

    /// <summary>Libellé du régime dans le système source, si connu.</summary>
    public string? Label { get; init; }

    /// <summary>Occurrences de la dernière observation (valeur indicative, non cumulée — PIV04 INV-015).</summary>
    public required long Occurrences { get; init; }

    /// <summary>Horodatage de la dernière observation (UTC).</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
