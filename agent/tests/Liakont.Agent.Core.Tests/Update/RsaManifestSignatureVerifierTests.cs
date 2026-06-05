namespace Liakont.Agent.Core.Tests.Update;

using System.Text;
using FluentAssertions;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>
/// Vérification de la signature du manifeste (décision D6, ADR-0013) : ce qui FONDE la confiance dans
/// l'auto-update. Couvre la chaîne complète signer (poste de release) → vérifier (agent), le rejet
/// d'un manifeste altéré, d'une autre clé, l'absence de clé (fail-closed) et une signature mal formée.
/// </summary>
public class RsaManifestSignatureVerifierTests
{
    private static readonly byte[] ManifestContent =
        Encoding.UTF8.GetBytes("{\"version\":\"2.0.0\",\"packageUrl\":\"https://x/p.zip\",\"packageSha256\":\"" + new string('a', 64) + "\"}");

    [Fact]
    public void A_signature_produced_by_the_release_key_is_accepted()
    {
        var signer = new TestUpdateSigner();
        string signature = signer.SignBase64(ManifestContent);
        var verifier = new RsaManifestSignatureVerifier(signer.PublicKeyXml);

        verifier.HasKey.Should().BeTrue();
        verifier.Verify(ManifestContent, signature).Should().BeTrue();
    }

    [Fact]
    public void A_tampered_manifest_fails_verification()
    {
        var signer = new TestUpdateSigner();
        string signature = signer.SignBase64(ManifestContent);
        var verifier = new RsaManifestSignatureVerifier(signer.PublicKeyXml);

        byte[] tampered = (byte[])ManifestContent.Clone();
        tampered[10] ^= 0xFF;

        verifier.Verify(tampered, signature).Should().BeFalse();
    }

    [Fact]
    public void A_signature_from_a_different_key_is_rejected()
    {
        var releaseKey = new TestUpdateSigner();
        var attackerKey = new TestUpdateSigner();
        string forgedSignature = attackerKey.SignBase64(ManifestContent);
        var verifier = new RsaManifestSignatureVerifier(releaseKey.PublicKeyXml);

        verifier.Verify(ManifestContent, forgedSignature).Should().BeFalse();
    }

    [Fact]
    public void Without_a_provisioned_key_no_update_is_trusted()
    {
        var verifier = new RsaManifestSignatureVerifier(null);

        verifier.HasKey.Should().BeFalse();
        verifier.Verify(ManifestContent, "n-importe-quoi").Should().BeFalse();
    }

    [Fact]
    public void A_malformed_signature_is_rejected_without_throwing()
    {
        var signer = new TestUpdateSigner();
        var verifier = new RsaManifestSignatureVerifier(signer.PublicKeyXml);

        verifier.Verify(ManifestContent, "pas du base64 !!!").Should().BeFalse();
    }
}
