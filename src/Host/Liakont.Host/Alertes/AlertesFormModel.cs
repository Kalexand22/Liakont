namespace Liakont.Host.Alertes;

/// <summary>
/// Saisie éditable du dispositif d'alerte. Seuls les seuils des règles ACTIVES sont éditables ici (éditer le
/// seuil d'une règle gelée n'aurait aucun effet) ; les seuils gelés sont préservés tels quels par le service
/// à l'enregistrement. Mutable (lié aux champs du formulaire), instance partagée avec la page.
/// </summary>
public sealed class AlertesFormModel
{
    /// <summary>Seuil « agent muet » (heures), règle active.</summary>
    public int AgentSilentHours { get; set; }

    /// <summary>Seuil « documents bloqués » (jours), règle active.</summary>
    public int BlockedDocumentsDays { get; set; }

    /// <summary>Seuil « rejets PA » (jours), règle active.</summary>
    public int PaRejectionsDays { get; set; }

    /// <summary>Envoyer les alertes critiques au contact d'alerte du tenant (F12 §5.3).</summary>
    public bool AlertTenantContact { get; set; }

    /// <summary>E-mail de contact d'alerte du tenant, ou <c>null</c>/vide pour aucun.</summary>
    public string? ContactEmailAlerte { get; set; }
}
