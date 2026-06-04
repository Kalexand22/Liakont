namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Réglage de tax report SOUHAITÉ, passé à <see cref="IPaClient.EnsureTaxReportSettingAsync"/>
/// (idempotent — F05 §2). Toutes les valeurs proviennent du paramétrage du tenant (CFG02), jamais
/// du code (CLAUDE.md n°2/7). Les champs requis côté PA (F05 §2, B.3 : <c>start_date</c>,
/// <c>type_operation</c>, <c>enterprise_size</c>) sont obligatoires ici.
/// </summary>
public sealed record PaTaxReportSettingRequest
{
    /// <summary>Code NAF/INSEE à déclarer (issu du paramétrage du tenant).</summary>
    public string? NafCode { get; init; }

    /// <summary>Date de début de publication à déclarer.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Type d'opération à déclarer.</summary>
    public required string TypeOperation { get; init; }

    /// <summary>Taille d'entreprise à déclarer.</summary>
    public required string EnterpriseSize { get; init; }

    /// <summary>Schéma d'identification du compte (F05 §2 : « 0002 » = SIREN).</summary>
    public string? CinScheme { get; init; }
}
