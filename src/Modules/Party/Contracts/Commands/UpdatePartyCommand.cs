namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record UpdatePartyCommand : ICommand
{
    public required Guid PartyId { get; init; }

    public required string LegalName { get; init; }

    public string? TradeName { get; init; }

    public string? TaxId { get; init; }

    public string? Notes { get; init; }
}
