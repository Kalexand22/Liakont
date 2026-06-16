namespace Liakont.Modules.Signature.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles;
using Xunit;

/// <summary>
/// Couvre les invariants de capacités de l'abstraction de signature (ADR-0027 ; INV-SIGPROV-1..5) :
/// <c>[Flags]</c> en valeurs distinctes (HasFlag correct), niveaux en ENSEMBLE (jamais un max ordonné),
/// transport ORTHOGONAL à la localisation, et capacité/niveau absent → résultat TYPÉ (jamais d'exception).
/// </summary>
public sealed class SignatureProviderCapabilitiesTests
{
    [Fact]
    public void SignatureMode_FlagsHaveDistinctPowerOfTwoValues_HasFlagIsCorrect()
    {
        // INV-SIGPROV-2 : un [Flags] avec OnSite=0 rendrait HasFlag(OnSite) toujours vrai. On vérifie
        // qu'un fournisseur OnSite ne « supporte » PAS Remote.
        var caps = new SignatureProviderCapabilities { ProviderName = "Fake", Mode = SignatureMode.OnSite };

        caps.Supports(SignatureMode.OnSite).Should().BeTrue();
        caps.Supports(SignatureMode.Remote).Should().BeFalse();
        ((int)SignatureMode.Remote).Should().Be(1);
        ((int)SignatureMode.OnSite).Should().Be(2);
    }

    [Fact]
    public void CompletionTransport_IsCombinable_AndOrthogonalToMode()
    {
        // INV-SIGPROV-3 : Webhook | Polling coexistent (webhook primaire + polling de réconciliation).
        var caps = new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            Mode = SignatureMode.Remote,
            CompletionTransport = CompletionTransport.Webhook | CompletionTransport.Polling,
        };

        caps.CompletionTransport.HasFlag(CompletionTransport.Webhook).Should().BeTrue();
        caps.CompletionTransport.HasFlag(CompletionTransport.Polling).Should().BeTrue();
        caps.CompletionTransport.HasFlag(CompletionTransport.Synchronous).Should().BeFalse();
    }

    [Fact]
    public void SupportedLevels_IsAMembershipSet_NotAnOrderedMaximum()
    {
        // INV-SIGPROV-4 : un compte SES | QES n'offre PAS AES (pas de « niveau ≥ AES »).
        var caps = new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            SupportedLevels = SignatureLevel.SES | SignatureLevel.QES,
        };

        caps.Supports(SignatureLevel.SES).Should().BeTrue();
        caps.Supports(SignatureLevel.QES).Should().BeTrue();
        caps.Supports(SignatureLevel.AES).Should().BeFalse("QES présent n'implique jamais AES");
    }

    [Fact]
    public void Recorded_IsAlwaysAvailable_EvenWithNoDeclaredLevels()
    {
        // Recorded (acceptation enregistrée sans signature, défaut ADR-0024) est toujours disponible.
        var caps = new SignatureProviderCapabilities { ProviderName = "Fake", SupportedLevels = SignatureLevel.None };

        caps.Supports(SignatureLevel.Recorded).Should().BeTrue();
        caps.Supports(SignatureLevel.SES).Should().BeFalse();
    }

    [Fact]
    public async Task RequestSignatureAsync_UnsupportedLevel_ReturnsTypedGap_NoException()
    {
        // INV-SIGPROV-5 : un niveau non licencié → résultat typé, jamais d'exception ni de blocage.
        var provider = new FakeSignatureProvider(new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            Mode = SignatureMode.Remote,
            SupportedLevels = SignatureLevel.SES,
        });

        var result = await provider.RequestSignatureAsync(new SignatureRequest
        {
            CompanyId = "tenant-a",
            DocumentId = "doc-1",
            RequestedLevel = SignatureLevel.QES,
            RequestedMode = SignatureMode.Remote,
        });

        result.State.Should().Be(SignatureRequestState.CapabilityNotSupported);
        result.CapabilityNotSupported.Should().NotBeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(SignatureCapability.QualifiedLevel);
        result.ProviderReference.Should().BeNull("aucune demande n'est soumise quand le niveau manque");
    }

    [Fact]
    public async Task RequestSignatureAsync_SupportedLevelAndMode_IsSubmitted()
    {
        var provider = new FakeSignatureProvider(new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            Mode = SignatureMode.Remote,
            CompletionTransport = CompletionTransport.Webhook,
            SupportedLevels = SignatureLevel.SES | SignatureLevel.AES,
        });

        var result = await provider.RequestSignatureAsync(new SignatureRequest
        {
            CompanyId = "tenant-a",
            DocumentId = "doc-1",
            RequestedLevel = SignatureLevel.AES,
            RequestedMode = SignatureMode.Remote,
        });

        result.State.Should().Be(SignatureRequestState.Submitted);
        result.CapabilityNotSupported.Should().BeNull();
        result.ProviderReference.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleWebhookAsync_WhenWebhookFlagAbsent_ReturnsTypedGap_NoException()
    {
        // INV-SIGPROV-3 : un capteur sur place (Synchronous, pas de Webhook) → NotSupported sur le webhook.
        var provider = new FakeSignatureProvider(new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            Mode = SignatureMode.OnSite,
            CompletionTransport = CompletionTransport.Synchronous,
        });

        var result = await provider.HandleWebhookAsync(new SignatureWebhookContext { RawBody = [1, 2, 3] });

        result.State.Should().Be(SignatureWebhookState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(SignatureCapability.WebhookCompletion);
    }

    [Fact]
    public async Task DownloadProofAsync_WhenProofDownloadUnsupported_ReturnsTypedGap_NoContent()
    {
        var provider = new FakeSignatureProvider(new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            SupportsProofDownload = false,
        });

        var result = await provider.DownloadProofAsync("ref-1");

        result.CapabilityNotSupported.Should().NotBeNull();
        result.CapabilityNotSupported!.Capability.Should().Be(SignatureCapability.ProofDownload);
        result.Content.Should().BeNull();
    }

    [Fact]
    public async Task DownloadProofAsync_WhenProofDownloadSupported_ReturnsContent()
    {
        var provider = new FakeSignatureProvider(new SignatureProviderCapabilities
        {
            ProviderName = "Fake",
            SupportsProofDownload = true,
        });

        var result = await provider.DownloadProofAsync("ref-1");

        result.Content.Should().NotBeNull();
        result.CapabilityNotSupported.Should().BeNull();
    }

    [Fact]
    public void CapabilityNotSupportedResult_OperatorMessage_IsFrench_AndJournalisable()
    {
        var gap = SignatureCapabilityNotSupportedResult.Create("Yousign", SignatureCapability.QualifiedLevel);

        gap.ProviderName.Should().Be("Yousign");
        gap.Capability.Should().Be(SignatureCapability.QualifiedLevel);
        gap.OperatorMessage.Should().Contain("Yousign");
        gap.OperatorMessage.Should().Contain("ne prend pas en charge");
        gap.OperatorMessage.Should().Contain("qualifiée");
    }
}
