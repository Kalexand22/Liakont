namespace Liakont.Modules.TenantSettings.Infrastructure.Seed;

using System.Text.Json;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Lit les fichiers de seed d'un dossier <c>deployments/&lt;client&gt;/</c> (F12-A §8.1) :
/// <c>tenant-profile.json</c> (profil + fiscal + planification + seuils) et <c>pa-accounts.json</c>
/// (comptes PA, sans secret). Lecture pure : aucune écriture, aucune logique métier.
/// </summary>
internal static class TenantSeedReader
{
    public const string ProfileFileName = "tenant-profile.json";
    public const string PaAccountsFileName = "pa-accounts.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<(TenantProfileSeed? Profile, IReadOnlyList<PaAccountSeed> PaAccounts)> ReadAsync(
        string seedDirectoryPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(seedDirectoryPath) || !Directory.Exists(seedDirectoryPath))
        {
            throw new NotFoundException($"Dossier de seed introuvable : « {seedDirectoryPath} ».");
        }

        var profile = await ReadProfileAsync(seedDirectoryPath, ct);
        var paAccounts = await ReadPaAccountsAsync(seedDirectoryPath, ct);
        return (profile, paAccounts);
    }

    private static async Task<TenantProfileSeed?> ReadProfileAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, ProfileFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        try
        {
            return JsonSerializer.Deserialize<TenantProfileSeed>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ConflictException($"Seed « {ProfileFileName} » illisible (JSON invalide) : {ex.Message}", ex);
        }
    }

    private static async Task<IReadOnlyList<PaAccountSeed>> ReadPaAccountsAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, PaAccountsFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, ct);
        try
        {
            return JsonSerializer.Deserialize<List<PaAccountSeed>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ConflictException($"Seed « {PaAccountsFileName} » illisible (JSON invalide) : {ex.Message}", ex);
        }
    }
}
