namespace Liakont.Modules.Signature.Domain.Entities;

/// <summary>
/// Entrée DURABLE de l'inbox de webhooks de signature (ADR-0029 §4 ; INV-YOUSIGN-4/5). L'événement brut
/// AUTHENTIFIÉ (HMAC vérifié) est persisté tenant-scopé AVANT la réponse 2xx ; le traitement lourd (download
/// preuve + rapatriement WORM) est ASYNCHRONE (drain par <c>TenantJobRunner</c>). Idempotence par
/// <c>(CompanyId, ProviderType, EventId)</c> — jamais <c>EventId</c> seul. Un crash après 2xx ne perd pas
/// l'événement : il reste <see cref="ProcessedAtUtc"/> = <c>null</c> jusqu'au drain.
/// </summary>
public sealed record SignatureWebhookInboxItem
{
    /// <summary>Identifiant technique de l'entrée d'inbox.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tenant propriétaire (clé d'isolation <c>company_id</c>).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Type de fournisseur (clé de registre, ex. « Yousign »).</summary>
    public required string ProviderType { get; init; }

    /// <summary>Identifiant d'événement côté fournisseur (composant de la clé d'idempotence).</summary>
    public required string EventId { get; init; }

    /// <summary>Référence de la demande côté fournisseur (pour relire le statut / la preuve au drain).</summary>
    public required string ProviderReference { get; init; }

    /// <summary>Octets EXACTS du corps reçu (preuve d'audit + retraitement éventuel).</summary>
    public required byte[] RawBody { get; init; }

    /// <summary>Horodatage UTC de réception (avant 2xx).</summary>
    public DateTimeOffset ReceivedAtUtc { get; init; }

    /// <summary>Horodatage UTC de traitement par le drain, ou <c>null</c> si non encore drainé.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; init; }

    /// <summary>Nombre de tentatives de drain (diagnostic).</summary>
    public int AttemptCount { get; init; }

    /// <summary>Dernière erreur de drain (diagnostic), ou <c>null</c>.</summary>
    public string? LastError { get; init; }
}
