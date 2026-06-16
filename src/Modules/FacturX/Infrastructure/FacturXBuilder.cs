namespace Liakont.Modules.FacturX.Infrastructure;

using System.IO;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Contracts;
using Liakont.Modules.FacturX.Domain;
using Liakont.Modules.FacturX.Infrastructure.Pdf;
using QuestPDF.Fluent;

/// <summary>
/// Implémentation du port <see cref="IFacturXBuilder"/> (FX04, ADR-0023 §3/§4). Construit le Factur-X
/// (PDF/A-3 + <c>factur-x.xml</c> CII embarqué) À PARTIR DU PIVOT SEUL (INV-FX-4), en trois temps :
/// (1) sérialisation CII maison (FX03, <see cref="ICrossIndustryInvoiceSerializer"/>) — BLOQUE sur un
/// document non conforme/réconciliable (fail-closed, CLAUDE.md n°3) ; (2) rendu visuel PDF/A-3b
/// (<see cref="FacturXPdfRenderer"/>, QuestPDF) ; (3) scellement : embarquement du <c>factur-x.xml</c>
/// (relation <c>/AFRelationship = Alternative</c>, INV-FX-3) + écriture du bloc XMP <c>fx:</c>, via la
/// passe <c>DocumentOperation</c> de QuestPDF (qui opère sur des fichiers). QuestPDF est CONFINÉE à cette
/// couche (INV-FX-1). Aucune <c>PaCapabilities</c>, aucun plug-in PA : la décision de générer appartient
/// au pipeline appelant (FX07).
/// </summary>
public sealed class FacturXBuilder : IFacturXBuilder
{
    private const string MimeTypeXml = "text/xml";

    private readonly ICrossIndustryInvoiceSerializer _serializer;

    /// <summary>Crée le builder avec le sérialiseur CII (FX03).</summary>
    /// <param name="serializer">Sérialiseur pivot → CII EN 16931 (COMFORT).</param>
    public FacturXBuilder(ICrossIndustryInvoiceSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
    }

    /// <inheritdoc />
    public Task<FacturXDocument> BuildAsync(PivotDocumentDto pivot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        cancellationToken.ThrowIfCancellationRequested();

        // (1) CII (FX03) : lève FacturXGenerationException si un BT obligatoire n'est ni porté ni
        // dérivable, ou si les agrégats ne se réconcilient pas (bloquer plutôt qu'émettre faux).
        byte[] crossIndustryInvoiceXml = _serializer.Serialize(pivot);

        // (2) rendu visuel PDF/A-3b depuis le pivot (projection locale — INV-FX-4).
        FacturXReadableModel readable = PivotReadableProjection.Project(pivot);
        byte[] visualPdf = FacturXPdfRenderer.Render(readable);

        // (3) scellement : embarquement du factur-x.xml + XMP fx:.
        byte[] sealedPdf = Seal(visualPdf, crossIndustryInvoiceXml);

        var document = new FacturXDocument(sealedPdf, BuildFileName(pivot.Number), crossIndustryInvoiceXml);
        return Task.FromResult(document);
    }

    // Passe de scellement QuestPDF (DocumentOperation) : opère sur des fichiers (pas d'API en mémoire en
    // 2025.7.4). On écrit le PDF visuel + le CII dans un répertoire temporaire isolé, on embarque le
    // factur-x.xml (relation Alternative) et on injecte le XMP fx:, puis on relit l'artefact scellé.
    private static byte[] Seal(byte[] visualPdf, byte[] crossIndustryInvoiceXml)
    {
        DirectoryInfo workDir = Directory.CreateTempSubdirectory("liakont-facturx-");
        try
        {
            string visualPath = Path.Combine(workDir.FullName, "visual.pdf");
            string ciiPath = Path.Combine(workDir.FullName, FacturXProfile.AttachmentFileName);
            string sealedPath = Path.Combine(workDir.FullName, "facturx.pdf");

            File.WriteAllBytes(visualPath, visualPdf);
            File.WriteAllBytes(ciiPath, crossIndustryInvoiceXml);

            DocumentOperation
                .LoadFile(visualPath)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    Key = FacturXProfile.AttachmentFileName,
                    FilePath = ciiPath,
                    AttachmentName = FacturXProfile.AttachmentFileName,
                    MimeType = MimeTypeXml,
                    Description = "Factur-X CII (EN 16931)",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Alternative,
                })
                .ExtendMetadata(FacturXXmpMetadata.Build())
                .Save(sealedPath);

            return File.ReadAllBytes(sealedPath);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static void TryDeleteDirectory(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Nettoyage best-effort d'un répertoire temporaire : ne jamais masquer l'artefact produit.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem (verrou antivirus/handle résiduel sous Windows) — sans incidence sur la sortie.
        }
    }

    // Nom de fichier proposé pour le PDF : numéro de document assaini (caractères de chemin neutralisés) +
    // « .pdf ». Présentation uniquement — aucune sémantique fiscale.
    private static string BuildFileName(string documentNumber)
    {
        var safe = documentNumber;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '-');
        }

        safe = safe.Trim();
        return string.IsNullOrEmpty(safe) ? "facture.pdf" : safe + ".pdf";
    }
}
