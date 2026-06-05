namespace Liakont.Modules.Staging.Contracts;

using System;

/// <summary>
/// Clé d'une entrée du magasin de staging (ADR-0014) : le tenant propriétaire, l'identifiant de document
/// attribué à l'intake, et l'empreinte canonique du payload (ADR-0007). La clé est <b>tenant-scopée</b> par
/// construction (blueprint §7 ; CLAUDE.md n°9) : un tenant ne peut jamais désigner l'entrée d'un autre.
/// L'empreinte n'entre pas dans le chemin de stockage — elle sert d'étiquette d'INTÉGRITÉ re-vérifiée à la
/// lecture (<see cref="IPayloadStagingStore.ReadAsync"/>), aucun WORM n'étant appliqué au staging.
/// </summary>
public sealed record StagedPayloadKey
{
    /// <summary>Crée une clé de staging. Tous les composants sont obligatoires.</summary>
    /// <param name="tenantId">Le tenant propriétaire (jamais vide).</param>
    /// <param name="documentId">L'identifiant de document attribué à l'intake (jamais <see cref="Guid.Empty"/>).</param>
    /// <param name="payloadHash">L'empreinte canonique SHA-256 du pivot (ADR-0007), étiquette d'intégrité.</param>
    public StagedPayloadKey(string tenantId, Guid documentId, string payloadHash)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Le tenant d'une entrée de staging est obligatoire (tenant-scopé).", nameof(tenantId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant de document d'une entrée de staging est obligatoire.", nameof(documentId));
        }

        if (string.IsNullOrWhiteSpace(payloadHash))
        {
            throw new ArgumentException("L'empreinte de payload d'une entrée de staging est obligatoire (intégrité).", nameof(payloadHash));
        }

        TenantId = tenantId;
        DocumentId = documentId;
        PayloadHash = payloadHash;
    }

    /// <summary>Le tenant propriétaire de l'entrée (jamais cross-tenant).</summary>
    public string TenantId { get; }

    /// <summary>L'identifiant de document attribué à l'intake (porté par <c>DocumentReceivedV1</c>).</summary>
    public Guid DocumentId { get; }

    /// <summary>L'empreinte canonique SHA-256 du pivot (ADR-0007), re-vérifiée à la lecture.</summary>
    public string PayloadHash { get; }
}
