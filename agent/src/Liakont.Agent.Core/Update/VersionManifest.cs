namespace Liakont.Agent.Core.Update;

using System;
using Newtonsoft.Json;

/// <summary>
/// Manifeste de version SIGNÉ (décision D6, ADR-0013) : la source de vérité de ce qu'est une mise à
/// jour légitime. Il référence le paquet ET son empreinte ; sa SIGNATURE (clé de release hors
/// plateforme) est vérifiée séparément contre la clé publique provisionnée. Les octets bruts du
/// manifeste sont l'unité signée — ce type n'est obtenu qu'APRÈS vérification de la signature.
/// </summary>
public sealed class VersionManifest
{
    private VersionManifest(string version, string packageUrl, string packageSha256, DateTime? releasedAtUtc)
    {
        Version = version;
        PackageUrl = packageUrl;
        PackageSha256 = packageSha256;
        ReleasedAtUtc = releasedAtUtc;
    }

    /// <summary>Version applicative publiée (ex. « 1.4.0 »).</summary>
    public string Version { get; }

    /// <summary>URL HTTPS du paquet de mise à jour (référencée par le manifeste signé).</summary>
    public string PackageUrl { get; }

    /// <summary>Empreinte SHA-256 attendue du paquet (hex minuscule, 64 caractères).</summary>
    public string PackageSha256 { get; }

    /// <summary>Date de publication déclarée (informative), si présente.</summary>
    public DateTime? ReleasedAtUtc { get; }

    /// <summary>
    /// Désérialise et VALIDE un manifeste à partir de ses octets bruts (UTF-8). Ne lève jamais :
    /// un manifeste illisible ou incomplet renvoie <c>false</c> (refus côté coordinateur).
    /// </summary>
    /// <param name="rawJsonUtf8">Octets bruts du manifeste (tels que signés).</param>
    /// <param name="manifest">Le manifeste validé, ou <c>null</c> si invalide.</param>
    /// <returns><c>true</c> si le manifeste est lisible et complet.</returns>
    public static bool TryParse(byte[] rawJsonUtf8, out VersionManifest? manifest)
    {
        manifest = null;
        if (rawJsonUtf8 == null || rawJsonUtf8.Length == 0)
        {
            return false;
        }

        try
        {
            string json = System.Text.Encoding.UTF8.GetString(rawJsonUtf8);
            ManifestDto? dto = JsonConvert.DeserializeObject<ManifestDto>(json);
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.Version)
                || string.IsNullOrWhiteSpace(dto.PackageUrl)
                || !IsSha256Hex(dto.PackageSha256))
            {
                return false;
            }

            manifest = new VersionManifest(
                dto.Version!.Trim(),
                dto.PackageUrl!.Trim(),
                dto.PackageSha256!.Trim().ToLowerInvariant(),
                dto.ReleasedAtUtc);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSha256Hex(string? value)
    {
        if (value == null)
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Length != 64)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    // DTO de désérialisation (Newtonsoft) — séparé pour garder VersionManifest immuable et validé.
    private sealed class ManifestDto
    {
        [JsonProperty("version")]
        public string? Version { get; set; }

        [JsonProperty("packageUrl")]
        public string? PackageUrl { get; set; }

        [JsonProperty("packageSha256")]
        public string? PackageSha256 { get; set; }

        [JsonProperty("releasedAtUtc")]
        public DateTime? ReleasedAtUtc { get; set; }
    }
}
