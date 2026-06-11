namespace Liakont.Host.Startup;

/// <summary>
/// Valeurs de publication du SIREN / tax_report_setting pour le seed de DÉVELOPPEMENT (section
/// <c>DevTenantSeed:TaxReportSetting</c>, appsettings.Development.json). FICTIVES (compte Fake, jamais une
/// vraie PA) — la place des valeurs d'exemple est la configuration de dev, jamais le code (CLAUDE.md n°7).
/// Permet à un environnement de dev vierge d'être transmissible sans geste manuel (décision E1, point 2).
/// </summary>
internal sealed class DevTenantTaxReportSettingOptions
{
    /// <summary>Date de début de publication au format <c>yyyy-MM-dd</c>. Vide = publication de dev désactivée.</summary>
    public string StartDate { get; init; } = string.Empty;

    /// <summary>Type d'opération à déclarer côté PA (valeur d'exemple pour le Fake).</summary>
    public string TypeOperation { get; init; } = string.Empty;

    /// <summary>Taille d'entreprise à déclarer côté PA (valeur d'exemple pour le Fake).</summary>
    public string EnterpriseSize { get; init; } = string.Empty;

    /// <summary>Code NAF/INSEE facultatif (valeur d'exemple), ou vide.</summary>
    public string? NafCode { get; init; }
}
