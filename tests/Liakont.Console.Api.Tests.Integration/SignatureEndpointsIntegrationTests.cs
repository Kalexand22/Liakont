namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints du module Signature (SIG08) : capture sur place
/// (<c>POST /api/v1/signature/onsite-capture</c>, garde <c>liakont.actions</c>) et enregistrement du
/// signataire vérifié (<c>POST /api/v1/signature/documents/{id}/verified-signer</c>, garde
/// <c>liakont.settings</c>). Vérifie le seam HTTP : permission (401/403), validation à la frontière (400),
/// tenant-scoping serveur (404 hors tenant), et résolution du <c>company_id</c> depuis le principal (anti-
/// usurpation ADR-0030 §3/§5).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class SignatureEndpointsIntegrationTests
{
    private const string OnSiteCapturePath = "/api/v1/signature/onsite-capture";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public SignatureEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string VerifiedSignerPath(Guid documentId) =>
        $"/api/v1/signature/documents/{documentId}/verified-signer";

    private static object ValidCaptureBody(Guid documentId) => new
    {
        documentId,
        signedBindingHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
        encryptedFssBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        signatureImagePngBase64 = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        declaredOperatorIdentity = "Opérateur salle",
        capturedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task OnSiteCapture_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.PostAsJsonAsync(OnSiteCapturePath, ValidCaptureBody(ConsoleApiFactory.TenantADocIssuedId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OnSiteCapture_With_ReadOnly_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsJsonAsync(OnSiteCapturePath, ValidCaptureBody(ConsoleApiFactory.TenantADocIssuedId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OnSiteCapture_With_Invalid_Base64_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);
        var body = new
        {
            documentId = ConsoleApiFactory.TenantADocIssuedId,
            signedBindingHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            encryptedFssBase64 = "pas-du-base64!!",
            signatureImagePngBase64 = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            declaredOperatorIdentity = "Opérateur salle",
            capturedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var response = await client.PostAsJsonAsync(OnSiteCapturePath, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OnSiteCapture_Authenticated_Operator_Reaches_Handler_Returns_200()
    {
        // L'endpoint résout company_id/uploader depuis le principal — sinon 400/403/404.
        // Aucune trace de support n'est seedée → BindingVerified peut être false (attendu) :
        // on vérifie le seam HTTP, pas le binding (couvert par les tests unitaires/intégration du handler).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(OnSiteCapturePath, ValidCaptureBody(ConsoleApiFactory.TenantADocIssuedId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CaptureResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Level.Should().Be("SES");
    }

    [Fact]
    public async Task VerifiedSigner_CrossTenant_Returns_404()
    {
        // Le document du tenant A est introuvable depuis le tenant B : tenant-scoping serveur.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.SettingsUserId);

        var response = await client.PostAsJsonAsync(
            VerifiedSignerPath(ConsoleApiFactory.TenantADocIssuedId),
            new { signerIdentity = "Mandant", verificationMethod = "en personne SVV" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task VerifiedSigner_SameTenant_Returns_201()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.SettingsUserId);

        var response = await client.PostAsJsonAsync(
            VerifiedSignerPath(ConsoleApiFactory.TenantADocIssuedId),
            new { signerIdentity = "Mandant Réel", verificationMethod = "identification en personne par la SVV" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task VerifiedSigner_Missing_Fields_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.SettingsUserId);

        var response = await client.PostAsJsonAsync(
            VerifiedSignerPath(ConsoleApiFactory.TenantADocIssuedId),
            new { signerIdentity = string.Empty, verificationMethod = string.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record CaptureResponse(Guid? ProofId, bool BindingVerified, bool SignerIdentityVerified, string Level, string Message);
}
