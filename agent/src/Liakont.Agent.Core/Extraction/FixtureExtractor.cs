namespace Liakont.Agent.Core.Extraction;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Newtonsoft.Json;

/// <summary>
/// Extracteur GÉNÉRIQUE rejouant des documents pivot depuis des données en mémoire ou un fichier JSON
/// (F01-F02 §4.4). Sert au développement sans licence source, aux tests et à la démo hors site —
/// SANS aucune dépendance ODBC. Comme tout extracteur, il n'a AUCUNE logique métier : il restitue les
/// documents fournis, filtrés par période sur leur date d'émission.
/// </summary>
public sealed class FixtureExtractor : IExtractor
{
    private readonly ExtractorInfo _info;
    private readonly List<PivotDocumentDto> _documents;
    private readonly List<PivotPaymentDto> _payments;
    private readonly List<SourceTaxRegimeDto> _regimes;
    private readonly List<SourceAttachment> _attachments;
    private readonly List<PoolDocument> _poolDocuments;

    /// <summary>Crée un extracteur de fixtures à partir de collections en mémoire.</summary>
    /// <param name="sourceName">Nom de la source simulée (ex. « Fixture »).</param>
    /// <param name="capabilities">Capacités déclarées de la source simulée.</param>
    /// <param name="documents">Documents pivot rejoués.</param>
    /// <param name="payments">Encaissements rejoués (F09).</param>
    /// <param name="sourceTaxRegimes">Régimes de TVA source restitués.</param>
    /// <param name="attachments">Pièces jointes liées (rendues seulement si la capacité PDF liés est déclarée).</param>
    /// <param name="poolDocuments">PDF du pool non lié (rendus seulement si la capacité pool est déclarée).</param>
    /// <param name="info">Identité de l'extracteur (par défaut, dérivée de <paramref name="sourceName"/>).</param>
    public FixtureExtractor(
        string sourceName,
        ExtractorCapabilities? capabilities = null,
        IEnumerable<PivotDocumentDto>? documents = null,
        IEnumerable<PivotPaymentDto>? payments = null,
        IEnumerable<SourceTaxRegimeDto>? sourceTaxRegimes = null,
        IEnumerable<SourceAttachment>? attachments = null,
        IEnumerable<PoolDocument>? poolDocuments = null,
        ExtractorInfo? info = null)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Le nom de la source de fixtures est requis.", nameof(sourceName));
        }

        SourceName = sourceName;
        Capabilities = capabilities ?? new ExtractorCapabilities();
        _documents = (documents ?? Enumerable.Empty<PivotDocumentDto>()).ToList();
        _payments = (payments ?? Enumerable.Empty<PivotPaymentDto>()).ToList();
        _regimes = (sourceTaxRegimes ?? Enumerable.Empty<SourceTaxRegimeDto>()).ToList();
        _attachments = (attachments ?? Enumerable.Empty<SourceAttachment>()).ToList();
        _poolDocuments = (poolDocuments ?? Enumerable.Empty<PoolDocument>()).ToList();
        _info = info ?? new ExtractorInfo(sourceName, "1.0.0", "Fixtures JSON (dev/démo/tests)");
    }

    /// <inheritdoc />
    public string SourceName { get; }

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <summary>Construit un extracteur de fixtures depuis un contenu JSON.</summary>
    /// <param name="json">Contenu JSON décrivant la source de fixtures.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static FixtureExtractor FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        FixtureData? data;
        try
        {
            data = JsonConvert.DeserializeObject<FixtureData>(json);
        }
        catch (JsonException ex)
        {
            throw new SourceSchemaException(
                $"Le fichier de fixtures n'est pas un JSON valide : {ex.Message}. Corrigez la syntaxe puis relancez.",
                ex);
        }

        if (data is null)
        {
            throw new SourceSchemaException("Le fichier de fixtures est vide. Renseignez au minimum sourceName et documents.");
        }

        return new FixtureExtractor(
            string.IsNullOrWhiteSpace(data.SourceName) ? "Fixture" : data.SourceName!,
            data.Capabilities,
            data.Documents,
            data.Payments,
            data.SourceTaxRegimes,
            data.Attachments,
            data.PoolDocuments,
            data.Info);
    }

    /// <summary>Construit un extracteur de fixtures depuis un fichier JSON.</summary>
    /// <param name="path">Chemin du fichier de fixtures.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static FixtureExtractor FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du fichier de fixtures est requis.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new SourceSchemaException($"Le fichier de fixtures est introuvable : « {path} ».");
        }

        return FromJson(File.ReadAllText(path));
    }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() => _info;

    /// <inheritdoc />
    public HealthCheckResult CheckHealth() =>
        HealthCheckResult.Healthy($"Source de fixtures « {SourceName} » : {_documents.Count} document(s) disponible(s).");

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        foreach (PivotDocumentDto document in _documents)
        {
            if (IsInPeriod(document.IssueDate, fromInclusiveUtc, toExclusiveUtc))
            {
                yield return document;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        foreach (PivotPaymentDto payment in _payments)
        {
            if (IsInPeriod(payment.PaymentDate, fromInclusiveUtc, toExclusiveUtc))
            {
                yield return payment;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes() => _regimes;

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        if (!Capabilities.ProvidesSourceDocuments || string.IsNullOrEmpty(sourceReference))
        {
            return Array.Empty<SourceAttachment>();
        }

        return _attachments
            .Where(a => string.Equals(a.SourceReference, sourceReference, StringComparison.Ordinal))
            .ToList();
    }

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        if (!Capabilities.ProvidesUnlinkedDocumentPool)
        {
            yield break;
        }

        // Le pool est un vrac de fichiers sans date fiable : la fixture restitue tout le pool déclaré
        // (l'adaptateur réel — lot ADP — décidera de la sémantique de date par fichier).
        foreach (PoolDocument document in _poolDocuments)
        {
            yield return document;
        }
    }

    private static bool IsInPeriod(DateTime value, DateTime fromInclusive, DateTime toExclusive) =>
        value >= fromInclusive && value < toExclusive;

    // Miroir brut du fichier de fixtures (désérialisation tolérante). Les DTO/types portés sont
    // construits par Newtonsoft via leur constructeur public (correspondance par nom de paramètre).
    private sealed class FixtureData
    {
        [JsonProperty("sourceName")]
        public string? SourceName { get; set; }

        [JsonProperty("info")]
        public ExtractorInfo? Info { get; set; }

        [JsonProperty("capabilities")]
        public ExtractorCapabilities? Capabilities { get; set; }

        [JsonProperty("documents")]
        public List<PivotDocumentDto>? Documents { get; set; }

        [JsonProperty("payments")]
        public List<PivotPaymentDto>? Payments { get; set; }

        [JsonProperty("sourceTaxRegimes")]
        public List<SourceTaxRegimeDto>? SourceTaxRegimes { get; set; }

        [JsonProperty("attachments")]
        public List<SourceAttachment>? Attachments { get; set; }

        [JsonProperty("poolDocuments")]
        public List<PoolDocument>? PoolDocuments { get; set; }
    }
}
