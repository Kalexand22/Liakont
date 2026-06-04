namespace Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Validation d'un code pays ISO 3166-1 alpha-2 (F04 §3.2). Un code est valide s'il fait partie des
/// 249 codes alpha-2 officiellement assignés par l'ISO 3166-1. Les codes « user-assigned » (ex. XK
/// pour le Kosovo) ne sont PAS reconnus : la liste est la norme, jamais une invention (CLAUDE.md
/// n°2) — toute évolution passe par une mise à jour explicite de cette liste de référence.
/// </summary>
public static class CountryCodeValidator
{
    private static readonly HashSet<string> OfficiallyAssigned = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT",
        "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI",
        "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY",
        "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN",
        "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM",
        "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK",
        "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL",
        "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM",
        "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR",
        "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN",
        "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS",
        "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK",
        "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW",
        "MX", "MY", "MZ", "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP",
        "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM",
        "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM",
        "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF",
        "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW",
        "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
        "VN", "VU", "WF", "WS", "YE", "YT", "ZA", "ZM", "ZW",
    };

    /// <summary>
    /// Indique si <paramref name="countryCode"/> est un code pays ISO 3166-1 alpha-2 officiellement
    /// assigné. La comparaison est insensible à la casse ; tout autre format est rejeté.
    /// </summary>
    /// <param name="countryCode">Le code pays à contrôler (absent = <c>null</c>).</param>
    /// <returns><c>true</c> si le code appartient à ISO 3166-1 alpha-2, sinon <c>false</c>.</returns>
    public static bool IsValid(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
        {
            return false;
        }

        return OfficiallyAssigned.Contains(countryCode);
    }
}
