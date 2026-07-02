namespace Liakont.Modules.Ged.Domain.Ingestion;

using System;

/// <summary>
/// Entrée du REGISTRE DE RÉCEPTION du canal GED (anti-doublon), item GED05b (F19 §4.3.1). Vit dans la base SYSTÈME
/// (schéma <c>ged_ingestion</c>), chaque ligne portant son <see cref="TenantId"/> — comme <c>ingestion.received_documents</c>,
/// mais dans un espace de hash STRICTEMENT SÉPARÉ (RL-01/§4.3.1). C'est un FAIT de réception : ajout uniquement,
/// jamais d'update/delete applicatif. L'empreinte <see cref="PayloadHash"/> est unique par tenant (anti-doublon) ;
/// une même <see cref="SourceReference"/> peut avoir plusieurs entrées au fil du temps (altérations successives).
/// AUCUN Document fiscal n'est associé — l'identité portée est celle du <see cref="ManagedDocumentId"/> (index GED).
/// </summary>
public sealed class GedReceivedDocument
{
    private GedReceivedDocument()
    {
    }

    /// <summary>Identité de l'entrée de registre.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (slug), issu de l'agent authentifié.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Référence du document dans le système source.</summary>
    public string SourceReference { get; private set; } = string.Empty;

    /// <summary>Empreinte canonique GED du payload (SHA-256 hex) — clé d'anti-doublon par tenant.</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    /// <summary>Identifiant du <c>ManagedDocument</c> attribué à la réception (index GED, idempotence RL-04).</summary>
    public Guid ManagedDocumentId { get; private set; }

    /// <summary>Version du contrat d'ingestion GED (<c>GedContractVersion</c>) — jamais une version fiscale.</summary>
    public string ContractVersion { get; private set; } = string.Empty;

    /// <summary>Horodatage de réception (UTC).</summary>
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    /// <summary>Crée une entrée de registre GED. Tenant / référence source / empreinte sont obligatoires.</summary>
    public static GedReceivedDocument Create(
        string tenantId,
        string sourceReference,
        string payloadHash,
        Guid managedDocumentId,
        string contractVersion,
        DateTimeOffset receivedAtUtc)
    {
        Require(tenantId, nameof(tenantId), "Le tenant est obligatoire.");
        Require(sourceReference, nameof(sourceReference), "La référence source est obligatoire.");
        Require(payloadHash, nameof(payloadHash), "L'empreinte du payload est obligatoire.");

        return new GedReceivedDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Trim(),
            SourceReference = sourceReference.Trim(),
            PayloadHash = payloadHash.Trim(),
            ManagedDocumentId = managedDocumentId,
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
