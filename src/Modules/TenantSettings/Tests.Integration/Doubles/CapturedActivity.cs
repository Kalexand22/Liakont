namespace Liakont.Modules.TenantSettings.Tests.Integration.Doubles;

/// <summary>Entrée d'activité capturée par <see cref="CapturingActivityLogger"/>.</summary>
internal sealed record CapturedActivity(
    string EntityType,
    string EntityId,
    string ActivityType,
    string ActorId,
    Guid? CompanyId);
