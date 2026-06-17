namespace Liakont.Modules.Signature.Tests.Integration;

using System;
using FluentAssertions;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Prouve l'isolation TENANT cross-BASE des stores du proxy OnSiteCapture sur DEUX bases réelles (CLAUDE.md
/// n°9 ; INV-ONSITE-5) : une preuve / liaison écrite dans la base d'un tenant n'est jamais visible depuis la
/// base de l'autre. Le scoping est par construction (la connexion EST le tenant — database-per-tenant).
/// </summary>
[Collection(SignatureCollectionFixture.Name)]
public sealed class OnSiteSignatureStoreTenantScopingIntegrationTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");

    private readonly SignatureMultiTenantFixture _fixture;

    public OnSiteSignatureStoreTenantScopingIntegrationTests(SignatureMultiTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Proof_WrittenInTenantA_IsNotVisibleFromTenantB()
    {
        var documentId = Guid.NewGuid();
        var storeA = new PostgresOnSiteSignatureProofStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA));
        var storeB = new PostgresOnSiteSignatureProofStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantB));

        await storeA.AppendAsync(new OnSiteSignatureProofRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            DocumentId = documentId,
            BindingHash = "ba7816bf",
            UploaderUserId = Guid.NewGuid(),
            SignerIdentity = "Mandant Réel",
            SignerVerified = true,
            Level = "SES",
            ProofArchiveRef = "package-hash",
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
        });

        (await storeA.FindLatestAsync(Company, documentId)).Should().NotBeNull();
        (await storeB.FindLatestAsync(Company, documentId)).Should().BeNull(
            "la preuve du tenant A n'existe pas dans la base du tenant B (isolation cross-base)");
    }

    [Fact]
    public async Task SignerBinding_RegisteredInTenantA_IsNotResolvableFromTenantB()
    {
        var documentId = Guid.NewGuid();
        var storeA = new PostgresOnSiteSignerBindingStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA));
        var storeB = new PostgresOnSiteSignerBindingStore(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantB));

        await storeA.RegisterAsync(new OnSiteSignerBindingRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            DocumentId = documentId,
            SignerIdentity = "Mandant Réel",
            VerificationMethod = "identification en personne par la SVV",
            RegisteredByUserId = Guid.NewGuid(),
            VerifiedAtUtc = DateTimeOffset.UnixEpoch,
        });

        (await storeA.ResolveVerifiedAsync(Company, documentId)).Should().NotBeNull();
        (await storeB.ResolveVerifiedAsync(Company, documentId)).Should().BeNull(
            "la liaison vérifiée du tenant A n'existe pas dans la base du tenant B (isolation cross-base)");
    }
}
