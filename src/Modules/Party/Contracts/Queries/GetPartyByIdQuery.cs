namespace Stratum.Modules.Party.Contracts.Queries;

using Stratum.Common.Abstractions.Messaging;
using Stratum.Modules.Party.Contracts.DTOs;

public record GetPartyByIdQuery(Guid PartyId) : IQuery<PartyDto?>;
