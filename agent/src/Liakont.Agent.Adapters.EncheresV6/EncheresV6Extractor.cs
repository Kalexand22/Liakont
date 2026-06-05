namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Plug-in source #1 (EncheresV6 — Magic XPA / Pervasive). PLACEHOLDER : la frontière
/// <see cref="IExtractor"/> est portée par AGT02, mais l'extraction ODBC réelle en lecture seule et
/// le mapping vers le pivot EN 16931 arrivent avec les items ADP (ADR-0004). Les méthodes de données
/// lèvent une <see cref="NotSupportedException"/> française explicite (jamais un échec silencieux) ;
/// celles qui doivent rester sans exception quand une capacité est absente (pièces jointes, pool)
/// renvoient une liste vide, conformément au contrat.
/// </summary>
public sealed class EncheresV6Extractor : IExtractor
{
    private const string NotImplementedMessage =
        "L'adaptateur EncheresV6 n'est pas encore opérationnel : l'extraction ODBC réelle est livrée par le lot ADP (ADR-0004).";

    /// <inheritdoc />
    public string SourceName => "EncheresV6";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; } = new ExtractorCapabilities();

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("EncheresV6", "0.0.0-placeholder", "Magic XPA / Pervasive (lot ADP)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth() => HealthCheckResult.Unhealthy(NotImplementedMessage);

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        throw new NotSupportedException(NotImplementedMessage);

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        throw new NotSupportedException(NotImplementedMessage);

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes() => Array.Empty<SourceTaxRegimeDto>();

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();
}
