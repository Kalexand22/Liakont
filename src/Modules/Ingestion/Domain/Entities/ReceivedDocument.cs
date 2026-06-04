namespace Liakont.Modules.Ingestion.Domain.Entities;

using System;

/// <summary>
/// Entrée du REGISTRE DE RÉCEPTION (anti-doublon) d'un document accepté par l'ingestion (F12 §3-4,
/// PIV04). Vit dans la base SYSTÈME (schéma <c>ingestion</c>), chaque ligne portant son
/// <see cref="TenantId"/> — comme le registre d'agents. C'est un FAIT de réception : ajout uniquement,
/// jamais d'update/delete applicatif. L'empreinte <see cref="PayloadHash"/> est unique par tenant
/// (anti-doublon) ; une même <see cref="SourceReference"/> peut avoir plusieurs entrées au fil du
/// temps (empreintes différentes = altérations successives de la source).
/// </summary>
public sealed class ReceivedDocument
{
    private ReceivedDocument()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (slug), issu de l'agent authentifié.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Référence du document dans le système source.</summary>
    public string SourceReference { get; private set; } = string.Empty;

    /// <summary>Empreinte canonique du payload (SHA-256 hex) — clé d'anti-doublon par tenant.</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    /// <summary>Identifiant du document métier créé en état <c>Detected</c> (module Documents).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Version de contrat négociée lors de la réception.</summary>
    public string ContractVersion { get; private set; } = string.Empty;

    /// <summary>Horodatage de réception (UTC).</summary>
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static ReceivedDocument Create(
        string tenantId,
        string sourceReference,
        string payloadHash,
        Guid documentId,
        string contractVersion,
        DateTimeOffset receivedAtUtc)
    {
        Require(tenantId, nameof(tenantId), "Le tenant est obligatoire.");
        Require(sourceReference, nameof(sourceReference), "La référence source est obligatoire.");
        Require(payloadHash, nameof(payloadHash), "L'empreinte du payload est obligatoire.");

        return new ReceivedDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Trim(),
            SourceReference = sourceReference.Trim(),
            PayloadHash = payloadHash.Trim(),
            DocumentId = documentId,
            ContractVersion = contractVersion,
            ReceivedAtUtc = receivedAtUtc,
        };
    }

    private static void Require(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }
}
