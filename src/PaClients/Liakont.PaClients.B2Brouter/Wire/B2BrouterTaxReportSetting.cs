namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Réglage de tax report DGFiP B2Brouter (F05 §2, RECAP B.3 :
/// <c>GET/POST/PATCH /accounts/{id}/tax_report_settings/dgfip.json</c>). DTO PROPRIÉTAIRE,
/// <c>internal</c>. Sert À LA FOIS de forme LUE (réponse) et de CORPS écrit (encapsulé dans
/// <see cref="B2BrouterTaxReportSettingRequest"/>). Snake_case (<see cref="B2BrouterJson"/>) :
/// <c>naf_code</c>, <c>start_date</c>, <c>type_operation</c>, <c>enterprise_size</c>, <c>cin_scheme</c>.
/// La date est portée en chaîne <c>yyyy-MM-dd</c> (forme « fil » B2Brouter, comme la date de facture).
/// Toutes les valeurs proviennent du paramétrage du tenant (CFG02), jamais du code (CLAUDE.md n°2/7).
/// </summary>
internal sealed record B2BrouterTaxReportSetting
{
    /// <summary>Code NAF/INSEE déclaré, ou <c>null</c>.</summary>
    public string? NafCode { get; init; }

    /// <summary>Date de début de publication au format <c>yyyy-MM-dd</c>, ou <c>null</c>.</summary>
    public string? StartDate { get; init; }

    /// <summary>Type d'opération déclaré, ou <c>null</c>.</summary>
    public string? TypeOperation { get; init; }

    /// <summary>Taille d'entreprise déclarée, ou <c>null</c>.</summary>
    public string? EnterpriseSize { get; init; }

    /// <summary>Schéma d'identification du compte (F05 §2 : « 0002 » = SIREN), ou <c>null</c>.</summary>
    public string? CinScheme { get; init; }
}
