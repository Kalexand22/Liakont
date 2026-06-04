namespace Liakont.Modules.TvaMapping.Domain.CoverageDetection;

/// <summary>
/// Vue compacte de la table de mapping d'un tenant nécessaire à la détection de couverture (item
/// TVA03) : la version, l'état de validation et l'ensemble des codes de régime source RÉFÉRENCÉS par
/// au moins une règle (toutes parts confondues). La couverture est évaluée au grain du CODE
/// (INV-012) ; le contrôle fin par (code, part) reste celui du moteur à l'exécution (TVA02, INV-007).
/// </summary>
public sealed record MappingTableSummary
{
    /// <summary>Version de la table de mapping confrontée.</summary>
    public required string MappingVersion { get; init; }

    /// <summary>État de validation humaine de la table (« NON VALIDÉE » = <c>false</c>, INV-006).</summary>
    public required bool IsValidated { get; init; }

    /// <summary>Codes de régime source couverts par au moins une règle de la table (doublons tolérés, dédupliqués à l'analyse).</summary>
    public required IReadOnlyCollection<string> MappedRegimeCodes { get; init; }
}
