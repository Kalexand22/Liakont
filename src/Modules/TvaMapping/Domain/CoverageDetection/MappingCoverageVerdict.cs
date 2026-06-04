namespace Liakont.Modules.TvaMapping.Domain.CoverageDetection;

/// <summary>
/// Verdict de couverture du mapping TVA pour un tenant (item TVA03, F03 §4.3).
/// </summary>
public enum MappingCoverageVerdict
{
    /// <summary>Tous les régimes source observés sont couverts par la table (ou aucun régime observé).</summary>
    Complete = 0,

    /// <summary>Au moins un régime source observé est absent de la table : la table doit être complétée.</summary>
    Incomplete = 1,
}
