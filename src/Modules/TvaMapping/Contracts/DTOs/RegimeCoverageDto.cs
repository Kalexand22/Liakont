namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Régime de TVA source observé pour un tenant, en lecture, tel que classé par la détection de
/// couverture (item TVA03, F03 §4.3). Valeur BRUTE — aucune interprétation fiscale.
/// </summary>
public record RegimeCoverageDto
{
    /// <summary>Code du régime dans le système source.</summary>
    public required string Code { get; init; }

    /// <summary>Libellé du régime source, <c>null</c> si inconnu.</summary>
    public string? Label { get; init; }

    /// <summary>Occurrences de la dernière observation (valeur indicative).</summary>
    public required long Occurrences { get; init; }

    /// <summary>Horodatage de la dernière observation (UTC).</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
