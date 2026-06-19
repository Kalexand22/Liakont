namespace Liakont.Modules.Signature.Tests.Integration;

using System;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Prouve, sur une base RÉELLE, que les journaux du module Signature sont APPEND-ONLY (ADR-0030 ; CLAUDE.md
/// n°4) : la base REJETTE tout UPDATE/DELETE d'une entrée existante ET tout TRUNCATE — garantie par double
/// trigger, indépendante du code applicatif.
/// </summary>
[Collection("SignatureMultiTenantIntegration")]
public sealed class OnSiteSignatureAppendOnlyIntegrationTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");

    private readonly SignatureMultiTenantFixture _fixture;

    public OnSiteSignatureAppendOnlyIntegrationTests(SignatureMultiTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Proof_Update_Delete_Truncate_AreRejected()
    {
        var factory = _fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA);
        var store = new PostgresOnSiteSignatureProofStore(factory);
        var documentId = Guid.NewGuid();
        await store.AppendAsync(new OnSiteSignatureProofRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            DocumentId = documentId,
            BindingHash = "ba7816bf",
            UploaderUserId = Guid.NewGuid(),
            SignerIdentity = null,
            SignerVerified = false,
            Level = "SES",
            ProofArchiveRef = "package-hash",
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
        });

        using var conn = (NpgsqlConnection)await factory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE signature.onsite_signature_proofs SET level = 'AES' WHERE document_id = @d", new { d = documentId });
        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");

        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM signature.onsite_signature_proofs WHERE document_id = @d", new { d = documentId });
        (await delete.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");

        var truncate = async () => await conn.ExecuteAsync("TRUNCATE signature.onsite_signature_proofs");
        (await truncate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task SignerBinding_Update_Delete_Truncate_AreRejected()
    {
        var factory = _fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA);
        var store = new PostgresOnSiteSignerBindingStore(factory);
        var documentId = Guid.NewGuid();
        await store.RegisterAsync(new OnSiteSignerBindingRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            DocumentId = documentId,
            SignerIdentity = "Mandant Réel",
            VerificationMethod = "identification en personne par la SVV",
            RegisteredByUserId = Guid.NewGuid(),
            VerifiedAtUtc = DateTimeOffset.UnixEpoch,
        });

        using var conn = (NpgsqlConnection)await factory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE signature.onsite_signer_bindings SET signer_identity = 'Usurpateur' WHERE document_id = @d", new { d = documentId });
        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");

        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM signature.onsite_signer_bindings WHERE document_id = @d", new { d = documentId });
        (await delete.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");

        var truncate = async () => await conn.ExecuteAsync("TRUNCATE signature.onsite_signer_bindings");
        (await truncate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
    }
}
