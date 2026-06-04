namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>Seuils d'alerte dans le seed (F12-A §6/§8.1). Champs absents = défaut produit F12-A §6.</summary>
internal sealed record ThresholdsSeed
{
    public int? AgentSilentHours { get; init; }

    public int? MissedRunHours { get; init; }

    public int? PushQueueMaxItems { get; init; }

    public int? PushQueueMaxAgeHours { get; init; }

    public int? BlockedDocumentsDays { get; init; }

    public int? PaRejectionsDays { get; init; }

    public bool AlertTenantContact { get; init; }
}
