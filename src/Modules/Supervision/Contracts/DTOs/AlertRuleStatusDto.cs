namespace Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// État d'UNE règle d'alerte du dispositif de supervision tel que présenté à l'opérateur (FIX210, F12 §5.2).
/// Restitution en lecture seule : la règle est active (évaluée) ou gelée (déclarée mais sans producteur de
/// données — SUP01c), avec sa gravité et son seuil effectif en clair.
/// </summary>
public record AlertRuleStatusDto
{
    /// <summary>Clé technique stable de la règle (F12 §5.2).</summary>
    public required string RuleKey { get; init; }

    /// <summary>Libellé opérateur français (F12 §5.2).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gravité en français (« Critique » / « Avertissement »).</summary>
    public required string Severity { get; init; }

    /// <summary><c>true</c> si la règle est réellement évaluée (SUP01b) ; <c>false</c> si gelée (SUP01c).</summary>
    public required bool IsActive { get; init; }

    /// <summary>Seuil effectif rendu en clair (ex. « &gt; 24 h », « J-3 », « — »).</summary>
    public required string ThresholdDisplay { get; init; }
}
