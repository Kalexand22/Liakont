namespace Liakont.Agent.Core.Tests.Update;

using System.Text;
using FluentAssertions;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>Lecture et validation du manifeste de version (ADR-0013). Ne lève jamais sur une entrée invalide.</summary>
public class VersionManifestTests
{
    private static readonly string ValidSha = new string('a', 64);

    [Fact]
    public void A_complete_manifest_is_parsed()
    {
        byte[] bytes = UpdateTestData.ManifestBytes("1.2.0", "https://updates.example/p.zip", ValidSha);

        VersionManifest.TryParse(bytes, out VersionManifest? manifest).Should().BeTrue();
        manifest!.Version.Should().Be("1.2.0");
        manifest.PackageUrl.Should().Be("https://updates.example/p.zip");
        manifest.PackageSha256.Should().Be(ValidSha);
    }

    [Fact]
    public void A_manifest_missing_the_package_url_is_rejected()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"version\":\"1.2.0\",\"packageSha256\":\"" + ValidSha + "\"}");

        VersionManifest.TryParse(bytes, out VersionManifest? manifest).Should().BeFalse();
        manifest.Should().BeNull();
    }

    [Fact]
    public void A_manifest_with_a_malformed_hash_is_rejected()
    {
        byte[] bytes = UpdateTestData.ManifestBytes("1.2.0", "https://updates.example/p.zip", "trop-court");

        VersionManifest.TryParse(bytes, out VersionManifest? manifest).Should().BeFalse();
        manifest.Should().BeNull();
    }

    [Fact]
    public void Garbage_bytes_are_rejected_without_throwing()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("ceci n'est pas du JSON");

        VersionManifest.TryParse(bytes, out VersionManifest? manifest).Should().BeFalse();
        manifest.Should().BeNull();
    }
}
