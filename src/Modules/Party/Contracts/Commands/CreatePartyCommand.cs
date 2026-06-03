namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record CreatePartyCommand : ICommand<Guid>
{
    public required string LegalName { get; init; }

    public string? TradeName { get; init; }

    public required string PartyType { get; init; }

    public string? TaxId { get; init; }

    public string? Notes { get; init; }
}
