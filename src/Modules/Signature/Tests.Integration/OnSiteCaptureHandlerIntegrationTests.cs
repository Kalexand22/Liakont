namespace Liakont.Modules.Signature.Tests.Integration;

using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Exerce le proxy OnSiteCapture DE BOUT EN BOUT contre des bases RÉELLES (≥ 2 tenants) : le binding
/// (<c>re-hash == hash signé</c> sur les octets exacts) commande la persistance dans le journal append-only
/// tenant-scopé (ADR-0030 §3/§4 ; INV-ONSITE-5/6). Les dépendances cross-module (document, artefact scellé,
/// coffre WORM) sont des stubs ; les stores Signature sont RÉELS (Postgres).
/// </summary>
[Collection(SignatureCollectionFixture.Name)]
public sealed class OnSiteCaptureHandlerIntegrationTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly byte[] SealedArtifact = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE-IT");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] FssBytes = Encoding.UTF8.GetBytes("FSS-DYNAMIQUE");

    private readonly SignatureMultiTenantFixture _fixture;

    public OnSiteCaptureHandlerIntegrationTests(SignatureMultiTenantFixture fixture) => _fixture = fixture;

    private static string HashHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DocumentDto Document(Guid id) => new()
    {
        Id = id,
        SourceReference = "src-1",
        DocumentNumber = "FA-IT-001",
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

    private static OnSiteCaptureCommand Command(Guid documentId, string signedHash) => new()
    {
        CompanyId = Company,
        UploaderUserId = Guid.NewGuid(),
        DocumentId = documentId,
        SignedBindingHash = signedHash,
        EncryptedFssBase64 = Convert.ToBase64String(FssBytes),
        SignatureImagePngBase64 = Convert.ToBase64String(PngBytes),
        DeclaredOperatorIdentity = "Opérateur salle",
        CapturedAtUtc = DateTimeOffset.UnixEpoch,
    };

    private OnSiteCaptureHandler HandlerFor(string tenantId, Guid documentId)
    {
        var factory = _fixture.CreateConnectionFactory(tenantId);
        return new OnSiteCaptureHandler(
            new StubDocumentQueries(Document(documentId)),
            new StubSupportTraceStore(SealedArtifact),
            new PostgresOnSiteSignerBindingStore(factory),
            new StubArchiveService(),
            new PostgresOnSiteSignatureProofStore(factory),
            new StubTenantContext(tenantId));
    }

    [Fact]
    public async Task BindingMatch_PersistsProof_InTenantA_IsolatedFromTenantB()
    {
        var documentId = Guid.NewGuid();
        var handler = HandlerFor(SignatureMultiTenantFixture.TenantA, documentId);

        var result = await handler.Handle(Command(documentId, HashHex(SealedArtifact)), CancellationToken.None);

        result.BindingVerified.Should().BeTrue();
        result.ProofId.Should().NotBeNull();
        result.Level.Should().Be("SES");

        var proofsA = new PostgresOnSiteSignatureProofStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA));
        var proofsB = new PostgresOnSiteSignatureProofStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantB));
        var stored = await proofsA.FindLatestAsync(Company, documentId);
        stored.Should().NotBeNull();
        stored!.BindingHash.Should().Be(HashHex(SealedArtifact));
        (await proofsB.FindLatestAsync(Company, documentId)).Should().BeNull("la preuve reste dans la base du tenant A");
    }

    [Fact]
    public async Task BindingMismatch_IsRejected_NoProofPersisted()
    {
        var documentId = Guid.NewGuid();
        var handler = HandlerFor(SignatureMultiTenantFixture.TenantA, documentId);
        var signedOverOther = HashHex(Encoding.UTF8.GetBytes("AUTRE-ARTEFACT"));

        var result = await handler.Handle(Command(documentId, signedOverOther), CancellationToken.None);

        result.BindingVerified.Should().BeFalse();
        result.ProofId.Should().BeNull();
        var proofsA = new PostgresOnSiteSignatureProofStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA));
        (await proofsA.FindLatestAsync(Company, documentId)).Should().BeNull("un binding non vérifié ne consigne aucune preuve");
    }
}
