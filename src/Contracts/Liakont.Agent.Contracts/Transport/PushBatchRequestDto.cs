namespace Liakont.Agent.Contracts.Transport;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Lot de documents poussé par l'agent vers la plateforme (POST /api/agent/v1/documents/batch —
/// F12 §3.4). Lot NON transactionnel ; la limite de taille (100 documents) est imposée par le
/// module Ingestion (PIV04), pas par ce DTO.
/// </summary>
public sealed class PushBatchRequestDto
{
    /// <summary>Crée une requête de lot.</summary>
    /// <param name="contractVersion">Version du contrat émise par l'agent (cf. <see cref="AgentContractVersion.ContractVersion"/>).</param>
    /// <param name="documents">Documents pivot du lot.</param>
    public PushBatchRequestDto(string contractVersion, IReadOnlyList<PivotDocumentDto>? documents = null)
    {
        ContractVersion = contractVersion;
        Documents = documents ?? Array.Empty<PivotDocumentDto>();
    }

    /// <summary>Version du contrat émise par l'agent.</summary>
    public string ContractVersion { get; }

    /// <summary>Documents pivot du lot.</summary>
    public IReadOnlyList<PivotDocumentDto> Documents { get; }
}
