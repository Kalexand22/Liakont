namespace Liakont.Modules.Signature.Tests.Unit.OnSite;

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Gardes du proxy OnSiteCapture (ADR-0030 §3/§4/§5/§8 ; INV-ONSITE-5/6/7/10) : tenant-scoping serveur (404
/// cross-tenant), binding <c>re-hash == hash signé</c> sur les octets exacts, signataire vérifié résolu par la
/// liaison séparée (JAMAIS le payload ni le déposant — test d'usurpation), rapatriement WORM de la preuve, et
/// absence de tout gabarit biométrique dérivé de la FSS.
/// </summary>
public sealed class OnSiteCaptureHandlerTests
{
    private static readonly Guid CompanyId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid DocumentId = Guid.Parse("0a000003-0000-0000-0000-000000000003");
    private static readonly Guid UploaderId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly byte[] SealedArtifact = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE-V1");
    private static readonly byte[] FssBytes = Encoding.UTF8.GetBytes("FSS-DYNAMIQUE-DU-STYLET");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static DocumentDto Document() => new()
    {
        Id = DocumentId,
        SourceReference = "src-1",
        DocumentNumber = "FA-A-001",
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 1, 10),
        CustomerIsCompanyHint = false,
        TotalNet = 100m,
        TotalTax = 20m,
        TotalGross = 120m,
        State = "Issued",
        PayloadHash = "payload-hash",
        FirstSeenUtc = DateTimeOffset.UnixEpoch,
        LastUpdateUtc = DateTimeOffset.UnixEpoch,
    };

    private static string HashHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static OnSiteCaptureCommand Command(string? signedHash = null, string? declaredOperator = null) => new()
    {
        CompanyId = CompanyId,
        UploaderUserId = UploaderId,
        DocumentId = DocumentId,
        SignedBindingHash = signedHash ?? HashHex(SealedArtifact),
        EncryptedFssBase64 = Convert.ToBase64String(FssBytes),
        SignatureImagePngBase64 = Convert.ToBase64String(PngBytes),
        DeclaredOperatorIdentity = declaredOperator,
        CapturedAtUtc = DateTimeOffset.UnixEpoch,
    };

    private static OnSiteCaptureHandler Handler(
        DocumentDto? document,
        byte[]? artifact,
        OnSiteSignerBindingRecord? verifiedBinding,
        out InMemoryOnSiteSignatureProofStore proofs,
        out RecordingArchiveService archive)
    {
        proofs = new InMemoryOnSiteSignatureProofStore();
        archive = new RecordingArchiveService();
        return new OnSiteCaptureHandler(
            new FakeDocumentQueries(document),
            new FakeSupportTraceStore(artifact),
            new FakeOnSiteSignerBindingStore(verifiedBinding),
            archive,
            proofs,
            new FakeTenantContext("tenant-a"));
    }

    private static OnSiteSignerBindingRecord VerifiedSigner(string identity) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyId,
        DocumentId = DocumentId,
        SignerIdentity = identity,
        VerificationMethod = "identification en personne par la SVV au guichet",
        RegisteredByUserId = Guid.NewGuid(),
        VerifiedAtUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Handle_UnknownDocument_ThrowsNotFound_CrossTenant()
    {
        // Document d'un autre tenant : introuvable dans la base du tenant courant → 404 (CLAUDE.md n°9).
        var handler = Handler(document: null, artifact: SealedArtifact, verifiedBinding: null, out _, out _);

        await FluentActions.Awaiting(() => handler.Handle(Command(), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SealedArtifactMissing_DoesNotVerifyBinding_NoProof()
    {
        var handler = Handler(Document(), artifact: null, verifiedBinding: null, out var proofs, out _);

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.BindingVerified.Should().BeFalse();
        result.ProofId.Should().BeNull();
        proofs.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BindingMismatch_IsRejected_NoProof()
    {
        // Le client a signé l'empreinte d'AUTRES octets que l'artefact scellé stocké.
        var signedOverOther = HashHex(Encoding.UTF8.GetBytes("AUTRE-ARTEFACT"));
        var handler = Handler(Document(), SealedArtifact, verifiedBinding: null, out var proofs, out _);

        var result = await handler.Handle(Command(signedHash: signedOverOther), CancellationToken.None);

        result.BindingVerified.Should().BeFalse();
        proofs.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BindingMatch_NoVerifiedSigner_AppendsProof_SignerUnverified()
    {
        var handler = Handler(Document(), SealedArtifact, verifiedBinding: null, out var proofs, out _);

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.BindingVerified.Should().BeTrue();
        result.SignerIdentityVerified.Should().BeFalse();
        result.Level.Should().Be("SES");
        var proof = proofs.Appended.Should().ContainSingle().Subject;
        proof.SignerVerified.Should().BeFalse();
        proof.SignerIdentity.Should().BeNull("aucune liaison vérifiée → signataire non prouvé");
        proof.UploaderUserId.Should().Be(UploaderId);
    }

    [Fact]
    public async Task Handle_Usurpation_SignerIdentityComesFromVerifiedBinding_NeverPayloadNorUploader()
    {
        // Le payload prétend une identité opérateur arbitraire ; la liaison vérifiée porte le vrai signataire.
        var handler = Handler(Document(), SealedArtifact, VerifiedSigner("Mandant Réel (SVV)"), out var proofs, out _);

        var result = await handler.Handle(
            Command(declaredOperator: "Usurpateur Déclaré"), CancellationToken.None);

        result.SignerIdentityVerified.Should().BeTrue();
        var proof = proofs.Appended.Should().ContainSingle().Subject;
        proof.SignerIdentity.Should().Be("Mandant Réel (SVV)");
        proof.SignerIdentity.Should().NotBe("Usurpateur Déclaré", "le signataire n'est JAMAIS dérivé du payload (INV-ONSITE-7)");
        proof.SignerIdentity.Should().NotBe(UploaderId.ToString(), "le signataire n'est JAMAIS le déposant");
    }

    [Fact]
    public async Task Handle_BindingMatch_RepatriatesProofPngToWorm()
    {
        var handler = Handler(Document(), SealedArtifact, verifiedBinding: null, out _, out var archive);

        await handler.Handle(Command(), CancellationToken.None);

        // Le dernier addendum archivé doit être le PNG (rendu lisible).
        archive.LastAddendum.Should().NotBeNull();
        archive.LastAddendum!.Kind.Should().Be("onsite-signature");
        archive.LastAddendum.Attachment.ContentType.Should().Be("image/png");
        archive.LastAddendum.Attachment.Content.Should().Equal(PngBytes, "le PNG est archivé en WORM comme rendu lisible");

        // La FSS chiffrée (artefact probant) doit également être archivée en WORM.
        var fssAddendum = archive.Addenda.Should().Contain(a => a.Kind == "onsite-signature-fss").Subject;
        fssAddendum.Attachment.Content.Should().Equal(FssBytes, "la FSS est rapatriée en WORM comme artefact probant (ADR-0030 §3)");
        fssAddendum.Attachment.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Handle_DoesNotDeriveBiometricTemplate_BindingHashesArtifactNotFss()
    {
        // RGPD sobre (INV-ONSITE-10) : l'empreinte de binding porte sur l'ARTEFACT scellé, jamais sur la FSS
        // (capture ≠ matching — aucun gabarit/feature-vector dérivé de la dynamique du stylet).
        // La FSS EST archivée verbatim comme preuve ; ce qui est interdit c'est d'en DÉRIVER un gabarit.
        var handler = Handler(Document(), SealedArtifact, verifiedBinding: null, out var proofs, out var archive);

        await handler.Handle(Command(), CancellationToken.None);

        var proof = proofs.Appended.Should().ContainSingle().Subject;
        proof.BindingHash.Should().Be(HashHex(SealedArtifact));
        proof.BindingHash.Should().NotBe(HashHex(FssBytes), "la FSS n'est jamais transformée en empreinte/gabarit stocké");

        // La FSS est conservée telle quelle comme preuve (verbatim, non transformée en gabarit).
        var fssAddendum = archive.Addenda.Should().Contain(a => a.Kind == "onsite-signature-fss").Subject;
        fssAddendum.Attachment.Content.Should().Equal(FssBytes, "la FSS est conservée telle quelle comme preuve, jamais dérivée en gabarit (capture ≠ matching, INV-ONSITE-10)");
    }
}
