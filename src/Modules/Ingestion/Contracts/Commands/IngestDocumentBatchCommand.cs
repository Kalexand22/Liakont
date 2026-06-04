namespace Liakont.Modules.Ingestion.Contracts.Commands;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Ingère un lot de documents poussé par un agent déjà authentifié (POST /api/agent/v1/documents/batch,
/// F12 §3.4, PIV04). Le lot est NON transactionnel : chaque document est évalué et persisté
/// indépendamment, et la réponse porte un résultat individuel par document (jamais de rejet global du
/// lot pour un seul document invalide).
/// </summary>
/// <remarks>
/// <see cref="AgentId"/> et <see cref="TenantId"/> proviennent de l'identité AUTHENTIFIÉE (posée par
/// le filtre d'authentification de l'API agent), jamais du corps de la requête : un agent ne peut
/// écrire que dans SON tenant (CLAUDE.md n°9, clé API scopée).
/// </remarks>
public sealed record IngestDocumentBatchCommand : ICommand<PushBatchResponseDto>
{
    /// <summary>Identifiant de l'agent authentifié (issu de l'identité, jamais du corps).</summary>
    public required Guid AgentId { get; init; }

    /// <summary>Tenant propriétaire de l'agent authentifié (slug ; issu de l'identité, jamais du corps).</summary>
    public required string TenantId { get; init; }

    /// <summary>Version de contrat négociée (en-tête <c>X-Contract-Version</c> validé par le filtre).</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Documents pivot du lot, dans l'ordre de la requête.</summary>
    public required IReadOnlyList<PivotDocumentDto> Documents { get; init; }

    /// <summary>Régimes de TVA source observés (métadonnée de push), persistés par tenant pour TVA03.</summary>
    public IReadOnlyList<SourceTaxRegimeDto> SourceTaxRegimes { get; init; } = System.Array.Empty<SourceTaxRegimeDto>();
}
