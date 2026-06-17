namespace Liakont.OnSiteSignature.Client.Tests;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.OnSiteSignature.Client.Tests.Fakes;
using Xunit;

/// <summary>
/// Gardes de l'orchestration de capture (ADR-0030 §3/§4 ; INV-ONSITE-6/10) : le payload lie l'empreinte à
/// l'ARTEFACT scellé (jamais à la FSS, qui est transmise telle quelle comme preuve), et la session POST
/// effectivement le payload construit (pur capteur — aucune décision).
/// </summary>
public sealed class OnSiteSignatureSessionTests
{
    private static readonly Guid DocumentId = Guid.Parse("0a000003-0000-0000-0000-000000000003");
    private static readonly byte[] SealedArtifact = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE");
    private static readonly byte[] Fss = Encoding.UTF8.GetBytes("FSS-DYNAMIQUE-DU-STYLET");
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly DateTimeOffset CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildPayload_BindsHashToSealedArtifact_NotToFss()
    {
        var capture = new CapturedSignature(Fss, Png);

        var payload = OnSiteSignatureSession.BuildPayload(
            DocumentId, SealedArtifact, capture, declaredOperatorIdentity: "Opérateur salle", CapturedAt);

        payload.DocumentId.Should().Be(DocumentId);
        payload.SignedBindingHash.Should().Be(BindingHasher.ComputeHex(SealedArtifact));
        payload.SignedBindingHash.Should().NotBe(BindingHasher.ComputeHex(Fss),
            "le binding porte sur l'artefact scellé, jamais sur la FSS (aucun gabarit dérivé — INV-ONSITE-10)");
        payload.EncryptedFssBase64.Should().Be(Convert.ToBase64String(Fss), "la FSS est transmise telle quelle (preuve), non transformée");
        payload.SignatureImagePngBase64.Should().Be(Convert.ToBase64String(Png));
        payload.DeclaredOperatorIdentity.Should().Be("Opérateur salle");
    }

    [Fact]
    public async Task CaptureAndSendAsync_CapturesThenPostsBuiltPayload()
    {
        var capture = new CapturedSignature(Fss, Png);
        var transport = new RecordingOnSiteCaptureTransport();
        var session = new OnSiteSignatureSession(new FakeSignaturePadDevice(capture), transport);

        var sent = await session.CaptureAndSendAsync(
            DocumentId, SealedArtifact, declaredOperatorIdentity: null, CapturedAt, CancellationToken.None);

        transport.LastPayload.Should().BeSameAs(sent, "la session POST exactement le payload construit");
        transport.LastPayload!.SignedBindingHash.Should().Be(BindingHasher.ComputeHex(SealedArtifact));
    }
}
