namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>Paramétrage fiscal dans le seed (F12-A §3/§8.1). Tous nullables (null = suspension).</summary>
internal sealed record FiscalSeed
{
    public bool? VatOnDebits { get; init; }

    public string? OperationCategory { get; init; }

    public string? ReportingFrequency { get; init; }
}
