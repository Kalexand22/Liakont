namespace Liakont.Modules.TenantSettings.Infrastructure;

using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Journalisation des mutations de paramétrage dans la piste d'audit append-only (module Audit),
/// avec l'identité de l'opérateur courant (F12-A §7, CLAUDE.md n°4). Appelée APRÈS le commit :
/// un échec d'écriture d'audit ne fait jamais échouer la transaction métier (INV-AUDIT-002).
/// </summary>
public sealed class TenantSettingsJournal
{
    private readonly IActivityLogger _activityLogger;
    private readonly IActorContextAccessor _actorContextAccessor;

    public TenantSettingsJournal(IActivityLogger activityLogger, IActorContextAccessor actorContextAccessor)
    {
        _activityLogger = activityLogger;
        _actorContextAccessor = actorContextAccessor;
    }

    public Task RecordAsync(
        string entityType,
        Guid entityId,
        string activityType,
        string description,
        Guid companyId,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var actorId = _actorContextAccessor.Current.UserId.ToString();
        return _activityLogger.LogActivityAsync(
            entityType,
            entityId.ToString(),
            activityType,
            description,
            actorId,
            metadata,
            companyId,
            ct);
    }
}
