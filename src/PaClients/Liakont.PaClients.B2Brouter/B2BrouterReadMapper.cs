namespace Liakont.PaClients.B2Brouter;

using System.Globalization;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Mappe les réponses de LECTURE B2Brouter (tax reports, réglage, compte — F05 §2) vers les DTOs
/// neutres de l'abstraction, et construit le corps d'écriture du réglage. Séparé de
/// <see cref="B2BrouterResponseMapper"/> (qui classe l'ENVOI) pour isoler le périmètre PAB03.
/// Règles de correction fiscale (CLAUDE.md n°2/3) :
/// <list type="bullet">
///   <item>Un état B2Brouter inconnu/absent est mappé au plus PRUDENT (<see cref="PaTaxReportState.New"/>),
///   JAMAIS <see cref="PaTaxReportState.Registered"/> (ne jamais affirmer « enregistré » à tort).</item>
///   <item>La réponse brute est conservée pour la piste d'audit (F06/DR6) sur les lectures unitaires.</item>
///   <item>Aucune valeur n'est inventée : un champ absent reste <c>null</c>.</item>
/// </list>
/// </summary>
internal static class B2BrouterReadMapper
{
    /// <summary>Mappe la liste des tax reports (réponse de <c>GET .../tax_reports.json</c>).</summary>
    /// <param name="body">Corps brut (tableau JSON). Vide/null → liste vide.</param>
    public static IReadOnlyList<PaTaxReport> MapTaxReports(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var wire = JsonSerializer.Deserialize<List<B2BrouterTaxReport>>(body, B2BrouterJson.Options);

        // Raw par item NON renseigné : la liste est une ÉNUMÉRATION ; la réponse brute par tax report
        // (audit) est portée par la lecture unitaire GetTaxReportAsync. La réponse brute du tableau ne
        // se découpe pas par item sans la réécrire (ce qui ne serait plus la donnée « fil » d'origine).
        return wire is null ? [] : wire.Select(w => ToContract(w, fallbackId: null, rawResponse: null)).ToList();
    }

    /// <summary>Mappe un tax report unitaire (réponse de <c>GET /tax_reports/{id}.json</c>).</summary>
    /// <param name="body">Corps brut (objet JSON), conservé pour l'audit.</param>
    /// <param name="fallbackId">Identifiant demandé, utilisé si la réponse n'en porte pas.</param>
    public static PaTaxReport MapTaxReport(string body, string fallbackId)
    {
        var wire = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<B2BrouterTaxReport>(body, B2BrouterJson.Options);

        return ToContract(
            wire ?? new B2BrouterTaxReport(),
            fallbackId,
            rawResponse: string.IsNullOrWhiteSpace(body) ? null : body);
    }

    /// <summary>Mappe le réglage de tax report (réponse de <c>GET .../tax_report_settings/dgfip.json</c>).</summary>
    /// <param name="body">Corps brut (objet JSON), conservé pour l'audit.</param>
    public static PaTaxReportSetting MapTaxReportSetting(string body)
    {
        var wire = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<B2BrouterTaxReportSetting>(body, B2BrouterJson.Options);

        return new PaTaxReportSetting
        {
            NafCode = wire?.NafCode,
            StartDate = ParseDate(wire?.StartDate),
            TypeOperation = wire?.TypeOperation,
            EnterpriseSize = wire?.EnterpriseSize,
            CinScheme = wire?.CinScheme,
            RawResponse = string.IsNullOrWhiteSpace(body) ? null : body,
        };
    }

    /// <summary>Mappe les informations de compte (réponse de <c>GET /accounts/{id}.json</c>).</summary>
    /// <param name="body">Corps brut (objet JSON), conservé pour l'audit.</param>
    /// <param name="fallbackAccountId">Identifiant de compte connu, utilisé si la réponse n'en porte pas.</param>
    public static PaAccountInfo MapAccountInfo(string body, string fallbackAccountId)
    {
        var wire = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<B2BrouterAccount>(body, B2BrouterJson.Options);

        return new PaAccountInfo
        {
            AccountId = string.IsNullOrWhiteSpace(wire?.Id) ? fallbackAccountId : wire!.Id!,
            TransactionsCount = wire?.TransactionsCount,
            TransactionsLimit = wire?.TransactionsLimit,
            RawResponse = string.IsNullOrWhiteSpace(body) ? null : body,
        };
    }

    /// <summary>Construit le corps d'écriture <c>{ "tax_report_setting": { … } }</c> à partir d'une demande.</summary>
    /// <param name="request">Réglage souhaité (valeurs issues du paramétrage du tenant).</param>
    public static B2BrouterTaxReportSettingRequest ToWire(PaTaxReportSettingRequest request) => new()
    {
        TaxReportSetting = new B2BrouterTaxReportSetting
        {
            NafCode = request.NafCode,
            StartDate = request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TypeOperation = request.TypeOperation,
            EnterpriseSize = request.EnterpriseSize,
            CinScheme = request.CinScheme,
        },
    };

    /// <summary>
    /// Vrai si le réglage courant correspond DÉJÀ à la demande (idempotence F05 §2 : aucune écriture
    /// si rien ne change). Compare champ à champ les valeurs significatives.
    /// </summary>
    /// <param name="current">Réglage lu côté PA.</param>
    /// <param name="desired">Réglage souhaité.</param>
    public static bool SettingMatches(PaTaxReportSetting current, PaTaxReportSettingRequest desired) =>
        current.NafCode == desired.NafCode
        && current.StartDate == desired.StartDate
        && current.TypeOperation == desired.TypeOperation
        && current.EnterpriseSize == desired.EnterpriseSize
        && current.CinScheme == desired.CinScheme;

    private static PaTaxReport ToContract(B2BrouterTaxReport wire, string? fallbackId, string? rawResponse) => new()
    {
        Id = string.IsNullOrWhiteSpace(wire.Id) ? (fallbackId ?? string.Empty) : wire.Id!,
        Type = wire.Type ?? string.Empty,
        Transport = wire.Transport,
        State = MapState(wire.State),
        XmlBase64 = wire.XmlBase64,
        HasErrors = wire.HasErrors ?? false,
        RawResponse = rawResponse,
    };

    // États F05 §3 : new → sent → acknowledged → registered. Un état inconnu/absent retombe sur New
    // (le moins avancé) — jamais « registered » par défaut, qui affirmerait à tort l'enregistrement
    // par l'administration (correction fiscale, CLAUDE.md n°3). Même prudence que le mapper d'envoi.
    private static PaTaxReportState MapState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "registered" => PaTaxReportState.Registered,
        "acknowledged" => PaTaxReportState.Acknowledged,
        "sent" => PaTaxReportState.Sent,
        "new" => PaTaxReportState.New,
        _ => PaTaxReportState.New,
    };

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
