namespace Liakont.Agent.Core.Tests.Update;

using Liakont.Agent.Core.Update;

/// <summary>Vérificateur de signature pilotable (clé présente/absente, signature valide/invalide).</summary>
internal sealed class StubManifestSignatureVerifier : IManifestSignatureVerifier
{
    public bool KeyPresent { get; set; } = true;

    public bool SignatureValid { get; set; } = true;

    public byte[]? VerifiedContent { get; private set; }

    public string? VerifiedSignature { get; private set; }

    public bool HasKey => KeyPresent;

    public bool Verify(byte[] content, string? signatureBase64)
    {
        VerifiedContent = content;
        VerifiedSignature = signatureBase64;
        return KeyPresent && SignatureValid;
    }
}
