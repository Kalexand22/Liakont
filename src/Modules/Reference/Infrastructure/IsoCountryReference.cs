namespace Liakont.Modules.Reference.Infrastructure;

/// <summary>
/// Liste des codes pays ISO 3166-1 alpha-2 officiellement assignés (249 codes), pour VALIDER la CIBLE d'une
/// correspondance à l'écriture (ADR-0038 §5 / INV-REF-CTRY-03) : seul un vrai code ISO est storable comme
/// cible d'alias (un « XX » garbage est refusé à l'écriture, pas seulement bloqué en aval par BT-55).
/// <para>
/// Cette liste DUPLIQUE volontairement la liste canonique de
/// <c>Liakont.Modules.Validation.Domain.Identity.CountryCodeValidator</c> : le module Reference ne référence
/// PAS Validation.Domain (frontière inter-modules, CLAUDE.md n°14 — un module n'accède à un autre que par ses
/// Contracts). Les deux dérivent de la MÊME norme internationale ISO 3166-1 alpha-2 (fait figé, pas une règle
/// inventée — CLAUDE.md n°2). Un test de PARITÉ (676 combinaisons + bords, Reference.Tests.Unit) verrouille
/// l'identité des deux listes et interdit toute dérive silencieuse ; la norme ISO étant stable, une évolution
/// (rare) met à jour les deux, gardée par ce test.
/// </para>
/// </summary>
internal static class IsoCountryReference
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
    /// Indique si <paramref name="countryCode"/> est un code pays ISO 3166-1 alpha-2 officiellement assigné.
    /// Comparaison insensible à la casse ; tout autre format (null, vide, longueur ≠ 2, hors norme) est rejeté.
    /// Logique IDENTIQUE à <c>CountryCodeValidator.IsValid</c> (garanti par le test de parité).
    /// </summary>
    public static bool IsValid(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
        {
            return false;
        }

        return OfficiallyAssigned.Contains(countryCode);
    }
}
