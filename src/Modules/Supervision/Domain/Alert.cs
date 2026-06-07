namespace Liakont.Modules.Supervision.Domain;

using System;

/// <summary>
/// Alerte de supervision (F12 §5) : un déclenchement d'une <c>IAlertRule</c> pour un tenant. C'est de
/// l'état OPÉRATIONNEL MUTABLE (résolution automatique + acquittement opérateur) — distinct de la piste
/// d'audit append-only (<c>DocumentEvent</c>) qui, elle, est immuable. Anti-bruit : au plus UNE alerte
/// active (<see cref="ResolvedUtc"/> nul) par (tenant, règle) ; une alerte active ne se re-déclenche pas,
/// elle se résout puis peut se re-déclencher (nouvelle ligne) après résolution.
/// </summary>
public sealed class Alert
{
    private Alert()
    {
    }

    /// <summary>Identifiant de l'alerte.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant concerné (slug). La supervision est le seul contexte cross-tenant du produit (lecture).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Clé stable de la règle qui a déclenché l'alerte (ex. <c>agent.mute</c>).</summary>
    public string RuleKey { get; private set; } = string.Empty;

    /// <summary>Gravité de l'alerte (F12 §5.2).</summary>
    public AlertSeverity Severity { get; private set; }

    /// <summary>Message opérateur actionnable (français), ou <c>null</c>.</summary>
    public string? Detail { get; private set; }

    /// <summary>Horodatage de déclenchement (UTC).</summary>
    public DateTimeOffset TriggeredUtc { get; private set; }

    /// <summary>Horodatage d'auto-résolution (UTC), ou <c>null</c> tant que l'alerte est active.</summary>
    public DateTimeOffset? ResolvedUtc { get; private set; }

    /// <summary>Identité de l'opérateur ayant acquitté l'alerte, ou <c>null</c>.</summary>
    public string? AcknowledgedBy { get; private set; }

    /// <summary>Horodatage d'acquittement (UTC), ou <c>null</c>.</summary>
    public DateTimeOffset? AcknowledgedUtc { get; private set; }

    /// <summary>Vrai tant que l'alerte n'est pas résolue.</summary>
    public bool IsActive => ResolvedUtc is null;

    /// <summary>Vrai si l'alerte a été acquittée par un opérateur.</summary>
    public bool IsAcknowledged => AcknowledgedBy is not null;

    /// <summary>Déclenche une nouvelle alerte ACTIVE pour un tenant et une règle.</summary>
    public static Alert Raise(
        string tenantId,
        string ruleKey,
        AlertSeverity severity,
        string? detail,
        DateTimeOffset nowUtc)
    {
        return new Alert
        {
            Id = Guid.NewGuid(),
            TenantId = RequireText(tenantId, nameof(tenantId)),
            RuleKey = RequireText(ruleKey, nameof(ruleKey)),
            Severity = severity,
            Detail = NullIfBlank(detail),
            TriggeredUtc = nowUtc,
            ResolvedUtc = null,
            AcknowledgedBy = null,
            AcknowledgedUtc = null,
        };
    }

    /// <summary>Reconstitue une alerte depuis la persistance (lecture).</summary>
    public static Alert Reconstitute(
        Guid id,
        string tenantId,
        string ruleKey,
        AlertSeverity severity,
        string? detail,
        DateTimeOffset triggeredUtc,
        DateTimeOffset? resolvedUtc,
        string? acknowledgedBy,
        DateTimeOffset? acknowledgedUtc)
    {
        return new Alert
        {
            Id = id,
            TenantId = tenantId,
            RuleKey = ruleKey,
            Severity = severity,
            Detail = detail,
            TriggeredUtc = triggeredUtc,
            ResolvedUtc = resolvedUtc,
            AcknowledgedBy = acknowledgedBy,
            AcknowledgedUtc = acknowledgedUtc,
        };
    }

    /// <summary>
    /// Résout automatiquement l'alerte (la condition a disparu). Idempotent serait masquant : on lève si
    /// l'alerte est déjà résolue — l'appelant (le moteur) ne résout qu'une alerte qu'il a trouvée active.
    /// </summary>
    public void Resolve(DateTimeOffset nowUtc)
    {
        if (ResolvedUtc is not null)
        {
            throw new InvalidOperationException(
                $"L'alerte « {RuleKey} » (tenant {TenantId}) est déjà résolue : on ne résout pas deux fois.");
        }

        ResolvedUtc = nowUtc;
    }

    /// <summary>
    /// Acquitte l'alerte au nom d'un opérateur (prise en charge, journalisée). N'affecte PAS la
    /// résolution : une alerte acquittée reste active tant que la condition persiste, et peut se résoudre
    /// ensuite. Acquitter à nouveau écrase l'acquittement (dernier opérateur en charge). Identité requise.
    /// </summary>
    public void Acknowledge(string operatorIdentity, DateTimeOffset nowUtc)
    {
        AcknowledgedBy = RequireText(operatorIdentity, nameof(operatorIdentity));
        AcknowledgedUtc = nowUtc;
    }

    private static string RequireText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Valeur obligatoire pour une alerte de supervision.", paramName);
        }

        return value.Trim();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
