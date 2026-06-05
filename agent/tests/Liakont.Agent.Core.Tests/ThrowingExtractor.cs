namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;

/// <summary>Extracteur de test qui lève une exception donnée sur <c>ExtractDocuments</c> (R7).</summary>
internal sealed class ThrowingExtractor : IExtractor
{
    private readonly Func<Exception> _onExtract;

    public ThrowingExtractor(Func<Exception> onExtract)
    {
        _onExtract = onExtract;
    }

    public string SourceName => "Throwing";

    public ExtractorCapabilities Capabilities { get; } = new ExtractorCapabilities();

    public ExtractorInfo GetInfo() => new ExtractorInfo("Throwing", "1.0.0", "Test");

    public HealthCheckResult CheckHealth() => HealthCheckResult.Healthy("test");

    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        throw _onExtract();

    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PivotPaymentDto>();

    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes() => Array.Empty<SourceTaxRegimeDto>();

    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();
}
