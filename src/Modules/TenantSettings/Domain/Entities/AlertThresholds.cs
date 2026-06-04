namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Seuils d'alerte de supervision d'un tenant (F12-A §6, consommés par SUP01). Paramétrables par
/// tenant, avec des valeurs par défaut PRODUIT traçables vers F12 §5.2 (pas des règles fiscales).
/// </summary>
public sealed class AlertThresholds
{
    /// <summary>Seuil « agent muet » par défaut (heures) — F12-A §6.</summary>
    public const int DefaultAgentSilentHours = 24;

    /// <summary>Seuil « run d'extraction manqué » par défaut (heures) — F12-A §6.</summary>
    public const int DefaultMissedRunHours = 36;

    /// <summary>Seuil « file de push » par défaut (éléments) — F12-A §6.</summary>
    public const int DefaultPushQueueMaxItems = 50;

    /// <summary>Seuil « file de push » par défaut (âge en heures) — F12-A §6.</summary>
    public const int DefaultPushQueueMaxAgeHours = 6;

    /// <summary>Seuil « documents bloqués non traités » par défaut (jours) — F12-A §6.</summary>
    public const int DefaultBlockedDocumentsDays = 5;

    /// <summary>Seuil « rejets PA non traités » par défaut (jours) — F12-A §6.</summary>
    public const int DefaultPaRejectionsDays = 2;

    private AlertThresholds()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int AgentSilentHours { get; private set; }

    public int MissedRunHours { get; private set; }

    public int PushQueueMaxItems { get; private set; }

    public int PushQueueMaxAgeHours { get; private set; }

    public int BlockedDocumentsDays { get; private set; }

    public int PaRejectionsDays { get; private set; }

    /// <summary>Active l'envoi des alertes critiques au contact d'alerte du tenant (F12-A §6).</summary>
    public bool AlertTenantContact { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Crée des seuils avec les valeurs par défaut produit (F12-A §6).</summary>
    public static AlertThresholds CreateDefault(Guid companyId)
    {
        return Create(
            companyId,
            DefaultAgentSilentHours,
            DefaultMissedRunHours,
            DefaultPushQueueMaxItems,
            DefaultPushQueueMaxAgeHours,
            DefaultBlockedDocumentsDays,
            DefaultPaRejectionsDays,
            alertTenantContact: false);
    }

    public static AlertThresholds Create(
        Guid companyId,
        int agentSilentHours,
        int missedRunHours,
        int pushQueueMaxItems,
        int pushQueueMaxAgeHours,
        int blockedDocumentsDays,
        int paRejectionsDays,
        bool alertTenantContact)
    {
        ValidatePositive(agentSilentHours, nameof(agentSilentHours));
        ValidatePositive(missedRunHours, nameof(missedRunHours));
        ValidatePositive(pushQueueMaxItems, nameof(pushQueueMaxItems));
        ValidatePositive(pushQueueMaxAgeHours, nameof(pushQueueMaxAgeHours));
        ValidatePositive(blockedDocumentsDays, nameof(blockedDocumentsDays));
        ValidatePositive(paRejectionsDays, nameof(paRejectionsDays));

        return new AlertThresholds
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            AgentSilentHours = agentSilentHours,
            MissedRunHours = missedRunHours,
            PushQueueMaxItems = pushQueueMaxItems,
            PushQueueMaxAgeHours = pushQueueMaxAgeHours,
            BlockedDocumentsDays = blockedDocumentsDays,
            PaRejectionsDays = paRejectionsDays,
            AlertTenantContact = alertTenantContact,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static AlertThresholds Reconstitute(
        Guid id,
        Guid companyId,
        int agentSilentHours,
        int missedRunHours,
        int pushQueueMaxItems,
        int pushQueueMaxAgeHours,
        int blockedDocumentsDays,
        int paRejectionsDays,
        bool alertTenantContact,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new AlertThresholds
        {
            Id = id,
            CompanyId = companyId,
            AgentSilentHours = agentSilentHours,
            MissedRunHours = missedRunHours,
            PushQueueMaxItems = pushQueueMaxItems,
            PushQueueMaxAgeHours = pushQueueMaxAgeHours,
            BlockedDocumentsDays = blockedDocumentsDays,
            PaRejectionsDays = paRejectionsDays,
            AlertTenantContact = alertTenantContact,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(
        int agentSilentHours,
        int missedRunHours,
        int pushQueueMaxItems,
        int pushQueueMaxAgeHours,
        int blockedDocumentsDays,
        int paRejectionsDays,
        bool alertTenantContact)
    {
        ValidatePositive(agentSilentHours, nameof(agentSilentHours));
        ValidatePositive(missedRunHours, nameof(missedRunHours));
        ValidatePositive(pushQueueMaxItems, nameof(pushQueueMaxItems));
        ValidatePositive(pushQueueMaxAgeHours, nameof(pushQueueMaxAgeHours));
        ValidatePositive(blockedDocumentsDays, nameof(blockedDocumentsDays));
        ValidatePositive(paRejectionsDays, nameof(paRejectionsDays));

        AgentSilentHours = agentSilentHours;
        MissedRunHours = missedRunHours;
        PushQueueMaxItems = pushQueueMaxItems;
        PushQueueMaxAgeHours = pushQueueMaxAgeHours;
        BlockedDocumentsDays = blockedDocumentsDays;
        PaRejectionsDays = paRejectionsDays;
        AlertTenantContact = alertTenantContact;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentException(
                $"INV-TENANTSETTINGS-002 : le seuil « {paramName} » doit être strictement positif.",
                paramName);
        }
    }
}
