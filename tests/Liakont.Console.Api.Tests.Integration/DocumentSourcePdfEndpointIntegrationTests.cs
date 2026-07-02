namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de <c>GET /api/v1/documents/{id}/piece-jointe</c> (consultation du PDF
/// d'origine reçu de l'agent) : enregistrement de la route sous <c>/api/v1</c>, garde <c>liakont.read</c>
/// (403 sans), isolation tenant (le document d'un AUTRE tenant est introuvable → 404), document sans PDF
/// (404, cas normal), et flux <c>application/pdf</c> servi <c>inline</c> + <c>nosniff</c> quand le PDF
/// existe — écrit par le VRAI store d'ingestion (même adressage que le push de l'agent).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentSourcePdfEndpointIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public DocumentSourcePdfEndpointIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SourcePdf_Without_Read_Permission_Returns_403()
    {
        // La garde liakont.read protège la diffusion du PDF d'origine (donnée fiscale du tenant).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.GetAsync($"/api/v1/documents/{ConsoleApiFactory.TenantADocIssuedId}/piece-jointe");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SourcePdf_Of_A_Document_From_Another_Tenant_Returns_404()
    {
        // Isolation PHYSIQUE (database-per-tenant) : l'identifiant d'un document du tenant B est
        // introuvable depuis le tenant A — aucune fuite cross-tenant via l'URL (CLAUDE.md n°9/17).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"/api/v1/documents/{ConsoleApiFactory.TenantBDocReadyId}/piece-jointe");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SourcePdf_When_No_Pdf_Was_Ingested_Returns_404()
    {
        // Document existant mais sans PDF poussé par l'agent : 404 explicite (cas normal, jamais un 500).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"/api/v1/documents/{ConsoleApiFactory.TenantADocReadyId}/piece-jointe");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SourcePdf_Streams_The_Ingested_Pdf_Inline_With_Nosniff()
    {
        // Écrit le PDF par le VRAI store d'ingestion (même adressage {tenant}/linked/{sha256(sourceReference)}
        // que le push de l'agent) pour le document émis seedé du tenant A (source_reference « src/AV-A-003 »).
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 test piece-jointe");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IIngestedPdfStore>();
            await using var content = new MemoryStream(pdfBytes, writable: false);
            await store.SaveLinkedPdfAsync(ConsoleApiFactory.TenantA, "src/AV-A-003", content);
        }

        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"/api/v1/documents/{ConsoleApiFactory.TenantADocIssuedId}/piece-jointe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline", "consultation dans le navigateur");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"')
            .Should().Be("AV-A-003.pdf", "nom lisible dérivé du n° de document, jamais le hash de stockage");
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff.Should().ContainSingle().Which.Should().Be("nosniff", "le type d'un fichier poussé par l'agent n'est jamais re-deviné par le navigateur");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(pdfBytes, "le flux servi est exactement le fichier ingéré");
    }
}
