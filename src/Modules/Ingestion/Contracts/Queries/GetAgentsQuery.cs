namespace Liakont.Modules.Ingestion.Contracts.Queries;

using Liakont.Modules.Ingestion.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Liste les agents du tenant courant (console et supervision, F12 §4.2/§5), sans jamais exposer
/// les clés. Le tenant est résolu côté handler via le contexte tenant.
/// </summary>
public sealed record GetAgentsQuery : IQuery<IReadOnlyList<AgentSummaryDto>>;
