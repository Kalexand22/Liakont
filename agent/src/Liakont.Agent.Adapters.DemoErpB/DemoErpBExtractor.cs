namespace Liakont.Agent.Adapters.DemoErpB;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Liakont.Agent.Adapters.DemoErpB.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Adaptateur de DÉMONSTRATION DemoErpB : lit une base SQL Server fictive (facturation DÉNORMALISÉE,
/// montants <c>float</c> legacy) en LECTURE SEULE STRICTE par ODBC (CLAUDE.md n°5, F01-F02 R1), et
/// transforme ses factures en pivot EN 16931 via <see cref="DemoErpBRowMapper"/> — la conversion
/// <c>float</c>→<c>decimal</c> half-up est faite à la frontière (ADR-0004 D3-7). Schéma volontairement
/// DIFFÉRENT de DemoErpA (anglais, acheteur en ligne, régime sur la ligne, types « I »/« C ») pour
/// exercer un second adaptateur réellement distinct. Idempotent (R2), aucune écriture.
/// </summary>
/// <remarks>
/// Note d'inversion (redline RD409, finding RD4-18) : NE PAS mapper la lettre du nom d'adaptateur
/// (DemoErpA / DemoErpB) sur la lettre du panel de validation de l'ADR-0004 (« Source A/B/C/D ») —
/// ce sont deux jeux INDÉPENDANTS. En particulier, l'ADR nomme « Source A » la source aux montants
/// <c>float</c> ; côté démo, c'est <b>DemoErpB</b> (cette classe) qui porte les montants <c>double</c>
/// legacy (+ leur conservation brute dans <c>SourceData</c>). Aucun changement de comportement attaché
/// à cette note : elle évite seulement une confusion de lecture.
/// </remarks>
public sealed class DemoErpBExtractor : IExtractor
{
    private const int QueryTimeoutSeconds = 30;

    private const string SourceUnavailableMessage =
        "La source DemoErpB est momentanément indisponible (connexion ou requête ODBC). Vérifiez que la base "
        + "et le pilote ODBC sont accessibles ; le prochain cycle d'extraction réessaiera automatiquement.";

    // Documents d'une période : entête + ses lignes (LEFT JOIN) + l'entête d'origine d'un avoir
    // (auto-jointure restreinte aux 'C'). Trié par InvoiceId puis LineNumber (regroupement STREAMING R8).
    // Période sur ModifiedUtc (horodatage de modification monotone). Bornes ODBC ? = [from, to[.
    private const string SelectDocumentsSql =
        "SELECT i.InvoiceId, i.InvoiceNo, i.Kind, i.IssuedOn, i.OriginInvoiceNo, "
        + "i.BuyerName, i.BuyerSiren, i.BuyerIsCompany, i.NetTotal, i.VatTotal, i.GrossTotal, i.Currency, "
        + "o.IssuedOn AS OriginIssuedOn, "
        + "it.LineNumber, it.Label, it.Qty, it.UnitPrice, it.NetAmount, it.VatAmount, it.VatRate, it.VatRegime "
        + "FROM dbo.Invoice i "
        + "LEFT JOIN dbo.Invoice o ON i.Kind = 'C' AND o.InvoiceNo = i.OriginInvoiceNo "
        + "LEFT JOIN dbo.InvoiceItem it ON it.InvoiceId = i.InvoiceId "
        + "WHERE i.ModifiedUtc >= ? AND i.ModifiedUtc < ? "
        + "ORDER BY i.InvoiceId, it.LineNumber";

    // Régimes observés (code BRUT, pas de table de libellés dans ce schéma) + occurrences. Lecture seule.
    private const string SelectTaxRegimesSql =
        "SELECT VatRegime AS code_regime, COUNT(*) AS occurrences "
        + "FROM dbo.InvoiceItem "
        + "GROUP BY VatRegime "
        + "ORDER BY VatRegime";

    private const string CountInvoicesSql = "SELECT COUNT(*) FROM dbo.Invoice";

    private readonly ISourceConnectionFactory _connectionFactory;
    private readonly IAgentLog _log;

    /// <summary>Crée l'extracteur DemoErpB.</summary>
    /// <param name="connectionFactory">Fabrique de connexions ODBC (lecture seule).</param>
    /// <param name="log">Journal (mise en quarantaine d'un document source malformé, sans figer la fenêtre).</param>
    public DemoErpBExtractor(ISourceConnectionFactory connectionFactory, IAgentLog log)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // R9 (gate « document finalisé », ADR-0004 D4 Famille 2) : la source de démonstration ne contient
        // que des pièces émises (pas d'état brouillon) → l'adaptateur s'engage à n'extraire que des
        // documents finalisés (extractsOnlyFinalizedDocuments: true).
        Capabilities = new ExtractorCapabilities(
            providesSourceDocuments: false,
            providesUnlinkedDocumentPool: false,
            hasDetailedLines: true,
            hasCreditNoteLink: true,
            exposesPayments: false,
            regimeKeyShape: RegimeKeyShape.Simple,
            emitterIdentitySource: EmitterIdentitySource.FilledByPlatform,
            hasStoredHeaderTotal: true,
            isMutableAfterIssue: false,
            numberUniquenessScope: NumberUniquenessScope.Global,
            extractsOnlyFinalizedDocuments: true);
    }

    /// <inheritdoc />
    public string SourceName => "DemoErpB";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("DemoErpB", "1.0.0-demo", "Base SQL Server de démonstration (facturation dénormalisée, float)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth()
    {
        try
        {
            using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
            using (IDbCommand command = SourceQuery.CreateSelect(connection, CountInvoicesSql, QueryTimeoutSeconds))
            {
                object? result = command.ExecuteScalar();
                long count = result is null || result == DBNull.Value
                    ? 0L
                    : Convert.ToInt64(result, CultureInfo.InvariantCulture);
                return HealthCheckResult.Healthy($"Source DemoErpB (ODBC, lecture seule) accessible — Invoice ({count}).");
            }
        }
        catch (SourceUnavailableException)
        {
            return HealthCheckResult.Unhealthy(
                "Connexion à la source DemoErpB impossible : vérifiez que le pilote ODBC est installé et que la "
                + "chaîne de connexion (login lecture seule) est correcte.");
        }
        catch (DbException)
        {
            return HealthCheckResult.Unhealthy(
                "Table source « Invoice » introuvable ou inaccessible : vérifiez le schéma DemoErpB et les droits du compte ODBC.");
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, SelectDocumentsSql, QueryTimeoutSeconds, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            DemoErpBInvoice? current = null;
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string invoiceId = OdbcCellReader.GetRequiredString(reader, "InvoiceId");
                if (current is null || !string.Equals(current.InvoiceId, invoiceId, StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        PivotDocumentDto? mapped = TryMapDocument(current);
                        if (mapped != null)
                        {
                            yield return mapped;
                        }
                    }

                    current = ReadHeader(reader, invoiceId);
                }

                if (!string.IsNullOrEmpty(OdbcCellReader.GetString(reader, "LineNumber")))
                {
                    current.Items.Add(ReadItem(reader));
                }
            }

            if (current != null)
            {
                PivotDocumentDto? mapped = TryMapDocument(current);
                if (mapped != null)
                {
                    yield return mapped;
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PivotPaymentDto>();

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes()
    {
        var regimes = new List<SourceTaxRegimeDto>();
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, SelectTaxRegimesSql, QueryTimeoutSeconds))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string? code = OdbcCellReader.GetString(reader, "code_regime");
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                regimes.Add(new SourceTaxRegimeDto(code!, label: null, occurrences: OdbcCellReader.GetInt(reader, "occurrences")));
            }
        }

        return regimes;
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();

    private static DemoErpBInvoice ReadHeader(IDataReader reader, string invoiceId) => new DemoErpBInvoice
    {
        InvoiceId = invoiceId,
        InvoiceNo = OdbcCellReader.GetString(reader, "InvoiceNo"),
        Kind = OdbcCellReader.GetString(reader, "Kind"),
        IssuedOn = OdbcCellReader.GetNullableDate(reader, "IssuedOn") ?? default(DateTime),
        OriginInvoiceNo = OdbcCellReader.GetString(reader, "OriginInvoiceNo"),
        OriginIssuedOn = OdbcCellReader.GetNullableDate(reader, "OriginIssuedOn"),
        Currency = OdbcCellReader.GetString(reader, "Currency"),
        NetTotal = OdbcCellReader.GetRequiredDouble(reader, "NetTotal"),
        VatTotal = OdbcCellReader.GetRequiredDouble(reader, "VatTotal"),
        GrossTotal = OdbcCellReader.GetRequiredDouble(reader, "GrossTotal"),
        BuyerName = OdbcCellReader.GetString(reader, "BuyerName"),
        BuyerSiren = OdbcCellReader.GetString(reader, "BuyerSiren"),
        BuyerIsCompany = OdbcCellReader.GetBool(reader, "BuyerIsCompany"),
    };

    private static DemoErpBItem ReadItem(IDataReader reader) => new DemoErpBItem
    {
        LineNumber = OdbcCellReader.GetString(reader, "LineNumber"),
        Label = OdbcCellReader.GetString(reader, "Label"),
        Qty = OdbcCellReader.GetNullableDouble(reader, "Qty") ?? 1.0,
        UnitPrice = OdbcCellReader.GetNullableDouble(reader, "UnitPrice"),
        NetAmount = OdbcCellReader.GetRequiredDouble(reader, "NetAmount"),
        VatAmount = OdbcCellReader.GetRequiredDouble(reader, "VatAmount"),
        VatRate = OdbcCellReader.GetNullableDouble(reader, "VatRate"),
        VatRegime = OdbcCellReader.GetString(reader, "VatRegime"),
    };

    // Isole un document source malformé : MapDocument lève SourceSchemaException (date absente, avoir
    // sans origine résoluble) sur UN document. On le met en quarantaine (journalisé) et on poursuit la
    // fenêtre — sinon l'exception interromprait l'itérateur AVANT l'avancée du filigrane et masquerait
    // tous les documents valides suivants (codex P2). Une erreur de LECTURE (colonne absente → schéma
    // incompatible) ou de connexion (SourceUnavailableException) n'est PAS interceptée ici : elle affecte
    // toute l'extraction (pas un seul document) et remonte donc — halt ou réessai de la fenêtre.
    private PivotDocumentDto? TryMapDocument(DemoErpBInvoice invoice)
    {
        try
        {
            return DemoErpBRowMapper.MapDocument(invoice);
        }
        catch (SourceSchemaException ex)
        {
            _log.Warn(
                $"Document source « {invoice.InvoiceNo ?? invoice.InvoiceId} » ignoré (quarantaine) : {ex.Message} "
                + "La fenêtre d'extraction se poursuit ; corrigez la source pour le réintégrer au prochain cycle.");
            return null;
        }
    }
}
