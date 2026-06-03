namespace Stratum.Modules.Party.Contracts.DTOs;

public record ImportPartyRow
{
    public required int RowNumber { get; init; }

    public required string LegalName { get; init; }

    public required string PartyType { get; init; }

    public string? TaxId { get; init; }

    public string? TradeName { get; init; }

    public string? AddressLine1 { get; init; }

    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string? CountryCode { get; init; }

    public string? Email { get; init; }
}
