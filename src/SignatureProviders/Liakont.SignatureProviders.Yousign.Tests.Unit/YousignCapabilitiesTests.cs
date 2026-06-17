namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Capacités DÉCLARÉES de Yousign (ADR-0029 §1 ; INV-YOUSIGN-1) : niveau réellement vérifié en sandbox (SES),
/// QES NON déclaré (→ NotSupported), fournisseur à distance à complétion par webhook + polling.
/// </summary>
public sealed class YousignCapabilitiesTests
{
    private static SignatureProviderCapabilities Caps => YousignCapabilities.Declared;

    [Fact]
    public void Declares_remote_mode_with_webhook_and_polling()
    {
        Caps.Mode.Should().Be(SignatureMode.Remote);
        Caps.Supports(SignatureMode.Remote).Should().BeTrue();
        Caps.Supports(SignatureMode.OnSite).Should().BeFalse();
        Caps.CompletionTransport.HasFlag(CompletionTransport.Webhook).Should().BeTrue();
        Caps.CompletionTransport.HasFlag(CompletionTransport.Polling).Should().BeTrue();
    }

    [Fact]
    public void Declares_only_ses_level_recorded_always_available_qes_not_supported()
    {
        Caps.Supports(SignatureLevel.SES).Should().BeTrue("SES est le niveau vérifié en sandbox");
        Caps.Supports(SignatureLevel.Recorded).Should().BeTrue("Recorded est toujours implicitement disponible");
        Caps.Supports(SignatureLevel.AES).Should().BeFalse("AES = activation au déploiement, jamais supposé");
        Caps.Supports(SignatureLevel.QES).Should().BeFalse("QES hors offre → jamais déclaré (NotSupported)");
    }

    [Fact]
    public void Declares_proof_download_for_worm_repatriation()
    {
        Caps.SupportsProofDownload.Should().BeTrue();
        Caps.SupportsBiometricCapture.Should().BeFalse("Yousign est à distance, pas un capteur sur place");
        Caps.SupportsBiometricTemplateMatching.Should().BeFalse();
    }
}
