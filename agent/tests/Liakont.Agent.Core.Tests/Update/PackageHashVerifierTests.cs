namespace Liakont.Agent.Core.Tests.Update;

using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>Vérification de l'empreinte SHA-256 du paquet (intégrité, ADR-0013).</summary>
public class PackageHashVerifierTests
{
    [Fact]
    public void Matches_is_true_when_the_file_hash_equals_the_expected_one()
    {
        using (var workspace = new TempDirectory())
        {
            byte[] package = UpdateTestData.MakeZipPackage();
            string path = workspace.Combine("package.zip");
            File.WriteAllBytes(path, package);
            string expected = UpdateTestData.Sha256Hex(package);

            PackageHashVerifier.ComputeSha256Hex(path).Should().Be(expected);
            PackageHashVerifier.Matches(path, expected).Should().BeTrue();
            PackageHashVerifier.Matches(path, expected.ToUpperInvariant()).Should().BeTrue("la comparaison est insensible à la casse");
        }
    }

    [Fact]
    public void Matches_is_false_for_a_wrong_or_empty_expected_hash()
    {
        using (var workspace = new TempDirectory())
        {
            byte[] package = UpdateTestData.MakeZipPackage();
            string path = workspace.Combine("package.zip");
            File.WriteAllBytes(path, package);

            PackageHashVerifier.Matches(path, new string('0', 64)).Should().BeFalse();
            PackageHashVerifier.Matches(path, null).Should().BeFalse();
            PackageHashVerifier.Matches(path, "   ").Should().BeFalse();
        }
    }
}
