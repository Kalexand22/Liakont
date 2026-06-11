namespace Liakont.Host.Alertes;

/// <summary>Valeurs des seuils éditables (règles actives) + activation du contact, transmises à l'enregistrement.</summary>
public sealed record AlertesThresholdInput
{
    /// <summary>Seuil « agent muet » (heures).</summary>
    public required int AgentSilentHours { get; init; }

    /// <summary>Seuil « documents bloqués » (jours).</summary>
    public required int BlockedDocumentsDays { get; init; }

    /// <summary>Seuil « rejets PA » (jours).</summary>
    public required int PaRejectionsDays { get; init; }

    /// <summary>Envoyer les alertes critiques au contact d'alerte du tenant.</summary>
    public required bool AlertTenantContact { get; init; }
}
