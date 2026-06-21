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

    public Func<HeartbeatRequestDto, HeartbeatOutcome>? OnSendHeartbeat { get; set; }

    public Func<ConfigurationOutcome>? OnGetConfiguration { get; set; }

    public List<PushedBatch> PushedBatches { get; } = new List<PushedBatch>();

    public List<HeartbeatRequestDto> Heartbeats { get; } = new List<HeartbeatRequestDto>();

    public int ConfigurationReads { get; private set; }

    public List<(string SourceReference, string PayloadHash)> StatusQueries { get; } = new List<(string, string)>();

    public List<(string SourceReference, string FilePath)> LinkedPdfPushes { get; } = new List<(string, string)>();

    public List<string> PoolPdfPushes { get; } = new List<string>();

    public PushBatchOutcome PushDocuments(IReadOnlyList<string> canonicalDocumentJsons, IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes, ExtractorCapabilitiesDto? extractorCapabilities = null)
    {
        PushedBatches.Add(new PushedBatch(canonicalDocumentJsons, sourceTaxRegimes, extractorCapabilities));
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

    public HeartbeatOutcome SendHeartbeat(HeartbeatRequestDto heartbeat)
    {
        Heartbeats.Add(heartbeat);
        return OnSendHeartbeat != null
            ? OnSendHeartbeat(heartbeat)
            : new HeartbeatOutcome(PlatformResponseKind.Ok, new AgentConfigurationDto());
    }

    public ConfigurationOutcome GetConfiguration()
    {
        ConfigurationReads++;
        return OnGetConfiguration != null
            ? OnGetConfiguration()
            : new ConfigurationOutcome(PlatformResponseKind.Ok, new AgentConfigurationDto());
    }

    internal sealed class PushedBatch
    {
        public PushedBatch(IReadOnlyList<string> documents, IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes, ExtractorCapabilitiesDto? extractorCapabilities)
        {
            Documents = documents;
            SourceTaxRegimes = sourceTaxRegimes;
            ExtractorCapabilities = extractorCapabilities;
        }

        public IReadOnlyList<string> Documents { get; }

        public IReadOnlyList<SourceTaxRegimeDto> SourceTaxRegimes { get; }

        public ExtractorCapabilitiesDto? ExtractorCapabilities { get; }
    }
}
