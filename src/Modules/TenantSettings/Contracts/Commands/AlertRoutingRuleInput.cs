namespace Liakont.Modules.TenantSettings.Contracts.Commands;

/// <summary>
/// Une entrée de matrice de routage des alertes à enregistrer (FIX212, F12 §5.3.1) : sélecteur (règle
/// et/ou gravité) + destinataires e-mail. Validée par le domaine (<c>AlertRoutingRule.Create</c>) au handler.
/// </summary>
public record AlertRoutingRuleInput
{
    /// <summary>Règle ciblée (F12 §5.2), ou <c>null</c>/vide pour « toute règle ».</summary>
    public string? RuleKey { get; init; }

    /// <summary>Gravité ciblée (<c>Warning</c>/<c>Critical</c>), ou <c>null</c>/vide pour « toute gravité ».</summary>
    public string? Severity { get; init; }

    /// <summary>Destinataires e-mail (au moins un).</summary>
    public required IReadOnlyList<string> Recipients { get; init; }
}
