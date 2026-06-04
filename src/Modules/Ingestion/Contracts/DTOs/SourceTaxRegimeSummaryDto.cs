namespace Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Vue de lecture (plateforme) d'un régime de TVA source observé pour un tenant, agrégé sur tous les
/// pushes (F12 / PIV04). Alimente la détection de couverture TVA (TVA03) : un régime source jamais
/// mappé dans la table du tenant doit être signalé. Valeur BRUTE — aucune interprétation fiscale ici.
/// </summary>
public sealed record SourceTaxRegimeSummaryDto
{
    /// <summary>Code du régime dans le système source (brut).</summary>
    public required string Code { get; init; }

    /// <summary>Libellé du régime dans le système source, si connu.</summary>
    public string? Label { get; init; }

    /// <summary>Occurrences de la DERNIÈRE observation (remplacée à chaque push, non cumulée — valeur indicative).</summary>
    public required long Occurrences { get; init; }

    /// <summary>Horodatage de la dernière observation (UTC).</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
