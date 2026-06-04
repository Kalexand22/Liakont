namespace Liakont.Modules.Validation.Domain;

/// <summary>
/// Référentiel des codes devise ISO 4217 (codes alphabétiques actifs) pour la validation de la devise
/// d'un document (F04 §3.3, EN 16931 BT-5). C'est une DONNÉE DE RÉFÉRENCE issue de la norme ISO 4217
/// / liste de codes genericode v17.0 (F04 §2.4) — pas une règle fiscale inventée (CLAUDE.md n°2) :
/// la liste reproduit les codes publiés par l'autorité de maintenance ISO 4217, y compris les codes
/// fonds (X-) et métaux précieux. La comparaison est insensible à la casse (les codes ISO 4217 sont
/// définis en majuscules ; on tolère la casse d'entrée).
/// </summary>
public static class Iso4217Currencies
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BOV",
        "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHE", "CHF", "CHW", "CLF", "CLP", "CNY", "COP", "COU",
        "CRC", "CUP", "CVE", "CZK",
        "DJF", "DKK", "DOP", "DZD",
        "EGP", "ERN", "ETB", "EUR",
        "FJD", "FKP",
        "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD",
        "HKD", "HNL", "HTG", "HUF",
        "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY",
        "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT",
        "LAK", "LBP", "LKR", "LRD", "LSL", "LYD",
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR",
        "MWK", "MXN", "MXV", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG",
        "QAR",
        "RON", "RSD", "RUB", "RWF",
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD",
        "SSP", "STN", "SVC", "SYP", "SZL",
        "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS",
        "UAH", "UGX", "USD", "USN", "UYI", "UYU", "UYW", "UZS",
        "VED", "VES", "VND", "VUV",
        "WST",
        "XAF", "XAG", "XAU", "XBA", "XBB", "XBC", "XBD", "XCD", "XDR", "XOF",
        "XPD", "XPF", "XPT", "XSU", "XUA",
        "YER",
        "ZAR", "ZMW", "ZWG", "ZWL",
    };

    /// <summary>Indique si <paramref name="code"/> est un code devise ISO 4217 valide (et non vide).</summary>
    /// <param name="code">Code devise à vérifier (ex. « EUR »).</param>
    /// <returns><c>true</c> si le code appartient à la liste ISO 4217.</returns>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Codes.Contains(code);
}
