namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>Planification d'extraction dans le seed (F12-A §5/§8.1).</summary>
internal sealed record ScheduleSeed
{
    public IReadOnlyList<string>? Hours { get; init; }

    public bool CatchUpOnStart { get; init; }
}
