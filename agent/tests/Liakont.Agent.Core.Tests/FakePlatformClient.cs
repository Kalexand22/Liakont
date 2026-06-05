namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Transport;

/// <summary>
/// Couture de plateforme scriptable pour les tests de drainage : chaque opération est pilotée par un
/// délégué (par défaut : succès « accepted »), et chaque appel est capturé pour les assertions
/// (lots poussés + régimes transmis, interrogations de statut, PDF poussés).
/// </summary>
internal sealed class FakePlatformClient : IPlatformClient
{
    public Func<IReadOnlyList<string>, IReadOnlyList<SourceTaxRegimeDto>, PushBatchOutcome>? OnPushDocuments { get; set; }

    public Func<string, string, DocumentStatusOutcome>? OnGetStatus { get; set; }

    public Func<string, string, PdfPushOutcome>? OnPushLinkedPdf { get; set; }

    public Func<string, PdfPushOutcome>? OnPushPoolPdf { get; set; }

    public List<PushedBatch> PushedBatches { get; } = new List<PushedBatch>();

    public List<(string SourceReference, string PayloadHash)> StatusQueries { get; } = new List<(string, string)>();

    public List<(string SourceReference, string FilePath)> LinkedPdfPushes { get; } = new List<(string, string)>();

    public List<string> PoolPdfPushes { get; } = new List<string>();

    public PushBatchOutcome PushDocuments(IReadOnlyList<string> canonicalDocumentJsons, IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes)
    {
        PushedBatches.Add(new PushedBatch(canonicalDocumentJsons, sourceTaxRegimes));
        return OnPushDocuments != null
            ? OnPushDocuments(canonicalDocumentJsons, sourceTaxRegimes)
            : new PushBatchOutcome(PlatformResponseKind.Ok);
    }

    public PdfPushOutcome PushLinkedPdf(string sourceReference, string filePath)
    {
        LinkedPdfPushes.Add((sourceReference, filePath));
        return OnPushLinkedPdf != null ? OnPushLinkedPdf(sourceReference, filePath) : new PdfPushOutcome(PlatformResponseKind.Ok);
    }

    public PdfPushOutcome PushPoolPdf(string filePath)
    {
        PoolPdfPushes.Add(filePath);
        return OnPushPoolPdf != null ? OnPushPoolPdf(filePath) : new PdfPushOutcome(PlatformResponseKind.Ok);
    }

    public DocumentStatusOutcome GetDocumentStatus(string sourceReference, string payloadHash)
    {
        StatusQueries.Add((sourceReference, payloadHash));
        return OnGetStatus != null
            ? OnGetStatus(sourceReference, payloadHash)
            : new DocumentStatusOutcome(PlatformResponseKind.Ok, DocumentIntakeStatus.Processed);
    }

    internal sealed class PushedBatch
    {
        public PushedBatch(IReadOnlyList<string> documents, IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes)
        {
            Documents = documents;
            SourceTaxRegimes = sourceTaxRegimes;
        }

        public IReadOnlyList<string> Documents { get; }

        public IReadOnlyList<SourceTaxRegimeDto> SourceTaxRegimes { get; }
    }
}
