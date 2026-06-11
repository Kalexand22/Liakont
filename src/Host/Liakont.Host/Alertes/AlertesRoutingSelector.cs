namespace Liakont.Host.Alertes;

using System;

/// <summary>
/// Encodage/décodage du sélecteur d'une entrée de matrice de routage (FIX212) entre la liste déroulante de
/// la vue (une seule valeur textuelle par ligne) et le couple (règle, gravité) du contrat TenantSettings.
/// Une ligne cible SOIT une règle précise (préfixe <see cref="RulePrefix"/>) SOIT une gravité
/// (préfixe <see cref="SeverityPrefix"/>). Centralisé pour que la vue et le service restent cohérents.
/// </summary>
internal static class AlertesRoutingSelector
{
    public const string RulePrefix = "rule:";
    public const string SeverityPrefix = "severity:";

    /// <summary>Jeton de gravité « critique » (🔴, F12 §5.2) — miroir du contrat de routage TenantSettings.</summary>
    public const string SeverityCriticalToken = "Critical";

    /// <summary>Jeton de gravité « avertissement » (🟠, F12 §5.2) — miroir du contrat de routage TenantSettings.</summary>
    public const string SeverityWarningToken = "Warning";

    public static string ForRule(string ruleKey) => RulePrefix + ruleKey;

    public static string ForSeverity(string severity) => SeverityPrefix + severity;

    /// <summary>Encode un couple (règle, gravité) lu en base vers la valeur de la liste déroulante (la règle prime).</summary>
    public static string Encode(string? ruleKey, string? severity)
    {
        if (!string.IsNullOrWhiteSpace(ruleKey))
        {
            return ForRule(ruleKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            return ForSeverity(severity.Trim());
        }

        return string.Empty;
    }

    /// <summary>Décode la valeur de la liste déroulante en (règle, gravité). Vide/inconnu ⇒ (null, null).</summary>
    public static (string? RuleKey, string? Severity) Decode(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return (null, null);
        }

        var value = selector.Trim();
        if (value.StartsWith(RulePrefix, StringComparison.Ordinal))
        {
            var ruleKey = value[RulePrefix.Length..].Trim();
            return (ruleKey.Length == 0 ? null : ruleKey, null);
        }

        if (value.StartsWith(SeverityPrefix, StringComparison.Ordinal))
        {
            var severity = value[SeverityPrefix.Length..].Trim();
            return (null, severity.Length == 0 ? null : severity);
        }

        return (null, null);
    }
}
