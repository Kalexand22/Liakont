namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>Adresse dans le seed (F12-A §2/§8.1).</summary>
internal sealed record AddressSeed
{
    public string? Street { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string? Country { get; init; }
}
