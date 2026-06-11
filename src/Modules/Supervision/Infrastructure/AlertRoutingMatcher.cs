namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Collections.Generic;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Évaluation PURE de la matrice de routage des alertes (F12 §5.3.1, FIX212) : à partir des entrées
/// tenant et d'une alerte (règle + gravité), produit la liste DÉDOUBLONNÉE des destinataires CÔTÉ TENANT
/// des entrées qui correspondent. Une entrée correspond si (sélecteur de règle absent OU égal à la règle)
/// ET (sélecteur de gravité absent OU égale à la gravité). Liste vide = aucune entrée applicable
/// (l'appelant retombe alors sur le modèle simple). Sans état ni I/O : testable directement.
/// </summary>
internal static class AlertRoutingMatcher
{
    public static IReadOnlyList<string> ResolveRecipients(
        IReadOnlyList<AlertRoutingRuleDto> matrix,
        string ruleKey,
        AlertSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Count == 0)
        {
            return [];
        }

        var severityToken = severity.ToString();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in matrix)
        {
            if (!Matches(entry, ruleKey, severityToken))
            {
                continue;
            }

            foreach (var recipient in entry.Recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    continue;
                }

                var trimmed = recipient.Trim();
                if (seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    private static bool Matches(AlertRoutingRuleDto entry, string ruleKey, string severityToken)
    {
        var hasSelector = !string.IsNullOrWhiteSpace(entry.RuleKey) || !string.IsNullOrWhiteSpace(entry.Severity);
        if (!hasSelector)
        {
            // Garde défensive : une entrée sans sélecteur ne route rien (le domaine l'interdit déjà).
            return false;
        }

        var ruleOk = string.IsNullOrWhiteSpace(entry.RuleKey)
            || string.Equals(entry.RuleKey, ruleKey, StringComparison.OrdinalIgnoreCase);
        var severityOk = string.IsNullOrWhiteSpace(entry.Severity)
            || string.Equals(entry.Severity, severityToken, StringComparison.OrdinalIgnoreCase);

        return ruleOk && severityOk;
    }
}
