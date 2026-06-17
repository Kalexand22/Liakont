namespace Liakont.Modules.Signature.Tests.Unit.OnSite;

using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Xunit;

/// <summary>
/// Gardes de capacités du fournisseur SUR PLACE (ADR-0030 §6/§8 ; INV-ONSITE-8/10). Le comportement est
/// piloté EXCLUSIVEMENT par les capacités : SES seulement (jamais AES/QES par défaut), capture biométrique
/// sans matching de gabarit, binding de hash, complétion synchrone, pas de webhook, pas de download de preuve
/// (rapatriée en WORM par Archive.Contracts, jamais le plug-in).
/// </summary>
public sealed class OnSiteSignatureProviderTests
{
    private static readonly SignatureProviderAccount Account = new("Wacom", "company-1");

    private static OnSiteSignatureProvider CreateProvider() =>
        (OnSiteSignatureProvider)new OnSiteSignatureProviderFactory().Create(Account);

    [Fact]
    public void Capabilities_AreSesOnSite_WithoutAesOrQes()
    {
        var caps = CreateProvider().Capabilities;

        caps.Mode.Should().Be(SignatureMode.OnSite);
        caps.Supports(SignatureLevel.SES).Should().BeTrue();
        caps.Supports(SignatureLevel.AES).Should().BeFalse("AES n'est offert qu'après audit du procédé (art. 26 c)");
        caps.Supports(SignatureLevel.QES).Should().BeFalse("Wacom seul ≠ dispositif + certificat qualifiés");
        caps.CompletionTransport.Should().Be(CompletionTransport.Synchronous);
    }

    [Fact]
    public void Capabilities_BiometricCaptureWithoutTemplateMatching()
    {
        var caps = CreateProvider().Capabilities;

        caps.SupportsBiometricCapture.Should().BeTrue("le tracé est capté comme preuve");
        caps.SupportsBiometricTemplateMatching.Should().BeFalse("RGPD sobre : aucun gabarit dérivé de la FSS (INV-ONSITE-10)");
        caps.SupportsDocumentHashBinding.Should().BeTrue();
    }

    [Fact]
    public async Task RequestSignature_OnSiteSes_IsSubmitted()
    {
        var result = await CreateProvider().RequestSignatureAsync(new SignatureRequest
        {
            CompanyId = "company-1",
            DocumentId = "doc-1",
            RequestedLevel = SignatureLevel.SES,
            RequestedMode = SignatureMode.OnSite,
        });

        result.State.Should().Be(SignatureRequestState.Submitted);
        result.ProviderReference.Should().Be("doc-1");
    }

    [Fact]
    public async Task RequestSignature_RemoteMode_IsNotSupported()
    {
        var result = await CreateProvider().RequestSignatureAsync(new SignatureRequest
        {
            CompanyId = "company-1",
            DocumentId = "doc-1",
            RequestedLevel = SignatureLevel.SES,
            RequestedMode = SignatureMode.Remote,
        });

        result.State.Should().Be(SignatureRequestState.CapabilityNotSupported);
    }

    [Fact]
    public async Task RequestSignature_QesLevel_IsNotSupported()
    {
        var result = await CreateProvider().RequestSignatureAsync(new SignatureRequest
        {
            CompanyId = "company-1",
            DocumentId = "doc-1",
            RequestedLevel = SignatureLevel.QES,
            RequestedMode = SignatureMode.OnSite,
        });

        result.State.Should().Be(SignatureRequestState.CapabilityNotSupported);
    }

    [Fact]
    public async Task DownloadProof_IsNotSupported_ProofGoesToWorm()
    {
        var proof = await CreateProvider().DownloadProofAsync("doc-1");

        proof.Content.Should().BeNull();
        proof.CapabilityNotSupported.Should().NotBeNull("la preuve est rapatriée par Archive.Contracts, jamais le plug-in");
    }

    [Fact]
    public void Factory_DeclaresWacomProviderType()
    {
        new OnSiteSignatureProviderFactory().ProviderType.Should().Be("Wacom");
    }
}
