namespace Liakont.Agent.Core.Tests.Update;

using System.IO;
using Liakont.Agent.Core.Update;

/// <summary>Source de mise à jour pilotable : sert des octets en mémoire, capture les URL demandées.</summary>
internal sealed class FakeUpdatePackageSource : IUpdatePackageSource
{
    public byte[]? ManifestBytes { get; set; }

    public byte[]? PackageBytes { get; set; }

    public bool FailManifest { get; set; }

    public bool FailPackage { get; set; }

    public string? RequestedManifestUrl { get; private set; }

    public string? RequestedPackageUrl { get; private set; }

    public byte[]? TryDownloadManifest(string manifestUrl)
    {
        RequestedManifestUrl = manifestUrl;
        return FailManifest ? null : ManifestBytes;
    }

    public bool TryDownloadPackage(string packageUrl, string destinationPath)
    {
        RequestedPackageUrl = packageUrl;
        if (FailPackage || PackageBytes == null)
        {
            return false;
        }

        File.WriteAllBytes(destinationPath, PackageBytes);
        return true;
    }
}
