namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Une entrée de la matrice de routage des alertes en lecture (F12 §5.3.1, FIX212). Sélecteur par règle
/// (<see cref="RuleKey"/>, F12 §5.2) et/ou par gravité (<see cref="Severity"/> = <c>Warning</c>/<c>Critical</c>),
/// vers une liste d'e-mails. Une matrice vide ⇒ modèle simple (défaut zéro-configuration).
/// </summary>
public record AlertRoutingRuleDto
{
    /// <summary>Règle ciblée (F12 §5.2), ou <c>null</c> pour « toute règle ».</summary>
    public string? RuleKey { get; init; }

    /// <summary>Gravité ciblée (<c>Warning</c>/<c>Critical</c>), ou <c>null</c> pour « toute gravité ».</summary>
    public string? Severity { get; init; }

    /// <summary>Destinataires e-mail (au moins un).</summary>
    public required IReadOnlyList<string> Recipients { get; init; }

    /// <summary>Rang d'affichage/évaluation (stable, 0..N-1).</summary>
    public required int Ordinal { get; init; }
}
