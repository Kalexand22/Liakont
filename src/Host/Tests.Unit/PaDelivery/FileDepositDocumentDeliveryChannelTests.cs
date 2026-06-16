namespace Liakont.Host.Tests.Unit.PaDelivery;

using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.PaDelivery;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Canal de DÉPÔT DE FICHIER (F16 §6.2) : écrit l'artefact dans le dossier du tenant, confine l'écriture
/// (aucune traversée de répertoire via le nom de fichier), et bloque sur un artefact vide.
/// </summary>
public sealed class FileDepositDocumentDeliveryChannelTests
{
    private static readonly byte[] SampleFacturX = Encoding.ASCII.GetBytes("%PDF-1.7 depot");

    [Fact]
    public void ResolveDestinationPath_Combines_Folder_And_File()
    {
        var path = FileDepositDocumentDeliveryChannel.ResolveDestinationPath(
            Path.Combine("depot", "tenant"), "factur-x_F-1.pdf");

        path.Should().Be(Path.Combine("depot", "tenant", "factur-x_F-1.pdf"));
    }

    [Fact]
    public void ResolveDestinationPath_Strips_Path_Segments_From_File_Name()
    {
        // Confinement : un nom de fichier porteur de « ../ » est réduit à son seul nom (anti-traversée).
        var path = FileDepositDocumentDeliveryChannel.ResolveDestinationPath(
            Path.Combine("depot", "tenant"), Path.Combine("..", "..", "evil.pdf"));

        path.Should().Be(Path.Combine("depot", "tenant", "evil.pdf"));
    }

    [Fact]
    public async Task DeliverAsync_Writes_The_Artifact_To_The_Tenant_Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "liakont-fxg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var channel = new FileDepositDocumentDeliveryChannel(
                NullLogger<FileDepositDocumentDeliveryChannel>.Instance);

            await channel.DeliverAsync(new DocumentDeliveryRequest
            {
                Method = DocumentDeliveryMethod.FileDeposit,
                Target = folder,
                Content = SampleFacturX,
                FileName = "factur-x_F-2026-009.pdf",
            });

            var written = Path.Combine(folder, "factur-x_F-2026-009.pdf");
            File.Exists(written).Should().BeTrue();
            (await File.ReadAllBytesAsync(written)).Should().Equal(SampleFacturX);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeliverAsync_Blocks_On_Empty_Artifact()
    {
        var channel = new FileDepositDocumentDeliveryChannel(
            NullLogger<FileDepositDocumentDeliveryChannel>.Instance);

        var act = async () => await channel.DeliverAsync(new DocumentDeliveryRequest
        {
            Method = DocumentDeliveryMethod.FileDeposit,
            Target = Path.GetTempPath(),
            Content = ReadOnlyMemory<byte>.Empty,
            FileName = "empty.pdf",
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
