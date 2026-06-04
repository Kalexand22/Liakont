namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

/// <summary>Compte PA dans le seed (F12-A §4/§8.1). La clé API n'est jamais lue ni stockée.</summary>
internal sealed record PaAccountSeed
{
    public string? PluginType { get; init; }

    public string? Environment { get; init; }

    public string? AccountIdentifiers { get; init; }
}
