namespace Liakont.Agent.Adapters.DemoErpA;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Liakont.Agent.Adapters.DemoErpA.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Adaptateur de DÉMONSTRATION DemoErpA : lit une base SQL Server fictive (ERP normalisé, montants
/// <c>decimal</c>) en LECTURE SEULE STRICTE par ODBC (CLAUDE.md n°5, F01-F02 R1), et transforme ses
/// factures en pivot EN 16931 via <see cref="DemoErpARowMapper"/>. Sert à éprouver le câblage AGT02
/// (ADR-0031) et l'installation d'un service contre une vraie source. Aucune écriture, aucun verrou,
/// aucune transaction d'écriture ; requêtes <c>SELECT</c> paramétrées avec timeout court. Idempotent
/// (R2 : la même période renvoie les mêmes <see cref="PivotDocumentDto.SourceReference"/>).
/// </summary>
public sealed class DemoErpAExtractor : IExtractor
{
    private const int QueryTimeoutSeconds = 30;

    private const string SourceUnavailableMessage =
        "La source DemoErpA est momentanément indisponible (connexion ou requête ODBC). Vérifiez que la base "
        + "et le pilote ODBC sont accessibles ; le prochain cycle d'extraction réessaiera automatiquement.";

    // Documents d'une période : entête + ses lignes (LEFT JOIN), + l'entête d'origine d'un avoir
    // (auto-jointure restreinte aux 'AVO'). Trié par facture_id puis no_ligne pour un regroupement en
    // STREAMING (R8). Période sur date_modif (horodatage de modification monotone). Bornes ODBC ? = [from, to[.
    private const string SelectDocumentsSql =
        "SELECT f.facture_id, f.numero, f.type_piece, f.date_emission, f.facture_origine_numero, "
        + "f.total_ht, f.total_tva, f.total_ttc, f.devise, "
        + "c.raison_sociale AS client_nom, c.siren AS client_siren, c.est_societe AS client_societe, "
        + "c.code_postal AS client_cp, c.ville AS client_ville, c.pays AS client_pays, "
        + "o.date_emission AS origine_date, "
        + "l.no_ligne, l.designation, l.quantite, l.prix_unitaire_ht, l.montant_ht, l.montant_tva, l.taux_tva, l.code_regime "
        + "FROM dbo.factures f "
        + "LEFT JOIN dbo.clients c ON c.client_id = f.client_id "
        + "LEFT JOIN dbo.factures o ON f.type_piece = 'AVO' AND o.numero = f.facture_origine_numero "
        + "LEFT JOIN dbo.lignes_facture l ON l.facture_id = f.facture_id "
        + "WHERE f.date_modif >= ? AND f.date_modif < ? "
        + "ORDER BY f.facture_id, l.no_ligne";

    // Régimes déclarés (code BRUT + libellé) + nombre d'occurrences observées dans les lignes. LEFT JOIN
    // pour qu'un régime jamais utilisé ressorte avec 0. Lecture seule (SELECT + GROUP BY).
    private const string SelectTaxRegimesSql =
        "SELECT r.code_regime, r.libelle, COUNT(l.code_regime) AS occurrences "
        + "FROM dbo.regimes_tva r "
        + "LEFT JOIN dbo.lignes_facture l ON l.code_regime = r.code_regime "
        + "GROUP BY r.code_regime, r.libelle "
        + "ORDER BY r.code_regime";

    private const string CountFacturesSql = "SELECT COUNT(*) FROM dbo.factures";

    private readonly ISourceConnectionFactory _connectionFactory;
    private readonly IAgentLog _log;

    /// <summary>Crée l'extracteur DemoErpA.</summary>
    /// <param name="connectionFactory">Fabrique de connexions ODBC (lecture seule).</param>
    /// <param name="log">Journal (mise en quarantaine d'un document source malformé, sans figer la fenêtre).</param>
    public DemoErpAExtractor(ISourceConnectionFactory connectionFactory, IAgentLog log)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
            numberUniquenessScope: NumberUniquenessScope.Global);
    }

    /// <inheritdoc />
    public string SourceName => "DemoErpA";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("DemoErpA", "1.0.0-demo", "Base SQL Server de démonstration (ERP normalisé, decimal)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth()
    {
        try
        {
            using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
            using (IDbCommand command = SourceQuery.CreateSelect(connection, CountFacturesSql, QueryTimeoutSeconds))
            {
                object? result = command.ExecuteScalar();
                long count = result is null || result == DBNull.Value
                    ? 0L
                    : Convert.ToInt64(result, CultureInfo.InvariantCulture);
                return HealthCheckResult.Healthy($"Source DemoErpA (ODBC, lecture seule) accessible — factures ({count}).");
            }
        }
        catch (SourceUnavailableException)
        {
            return HealthCheckResult.Unhealthy(
                "Connexion à la source DemoErpA impossible : vérifiez que le pilote ODBC est installé et que la "
                + "chaîne de connexion (login lecture seule) est correcte.");
        }
        catch (DbException)
        {
            return HealthCheckResult.Unhealthy(
                "Table source « factures » introuvable ou inaccessible : vérifiez le schéma DemoErpA et les droits du compte ODBC.");
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, SelectDocumentsSql, QueryTimeoutSeconds, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            DemoErpAInvoice? current = null;
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string factureId = OdbcCellReader.GetRequiredString(reader, "facture_id");
                if (current is null || !string.Equals(current.FactureId, factureId, StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        PivotDocumentDto? mapped = TryMapDocument(current);
                        if (mapped != null)
                        {
                            yield return mapped;
                        }
                    }

                    current = ReadHeader(reader, factureId);
                }

                // LEFT JOIN : une facture sans ligne produit une ligne d'entête seule (no_ligne NULL) — le
                // document est tout de même émis (jamais d'omission silencieuse).
                if (!string.IsNullOrEmpty(OdbcCellReader.GetString(reader, "no_ligne")))
                {
                    current.Lignes.Add(ReadLine(reader));
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

                regimes.Add(new SourceTaxRegimeDto(
                    code!,
                    OdbcCellReader.GetString(reader, "libelle"),
                    OdbcCellReader.GetInt(reader, "occurrences")));
            }
        }

        return regimes;
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();

    private static DemoErpAInvoice ReadHeader(IDataReader reader, string factureId) => new DemoErpAInvoice
    {
        FactureId = factureId,
        Numero = OdbcCellReader.GetString(reader, "numero"),
        TypePiece = OdbcCellReader.GetString(reader, "type_piece"),
        DateEmission = OdbcCellReader.GetNullableDate(reader, "date_emission") ?? default(DateTime),
        FactureOrigineNumero = OdbcCellReader.GetString(reader, "facture_origine_numero"),
        OrigineDate = OdbcCellReader.GetNullableDate(reader, "origine_date"),
        Devise = OdbcCellReader.GetString(reader, "devise"),
        TotalHt = OdbcCellReader.GetRequiredDecimal(reader, "total_ht"),
        TotalTva = OdbcCellReader.GetRequiredDecimal(reader, "total_tva"),
        TotalTtc = OdbcCellReader.GetRequiredDecimal(reader, "total_ttc"),
        ClientNom = OdbcCellReader.GetString(reader, "client_nom"),
        ClientSiren = OdbcCellReader.GetString(reader, "client_siren"),
        ClientEstSociete = OdbcCellReader.GetBool(reader, "client_societe"),
        ClientCodePostal = OdbcCellReader.GetString(reader, "client_cp"),
        ClientVille = OdbcCellReader.GetString(reader, "client_ville"),
        ClientPays = OdbcCellReader.GetString(reader, "client_pays"),
    };

    private static DemoErpALine ReadLine(IDataReader reader) => new DemoErpALine
    {
        NoLigne = OdbcCellReader.GetString(reader, "no_ligne"),
        Designation = OdbcCellReader.GetString(reader, "designation"),
        Quantite = OdbcCellReader.GetNullableDecimal(reader, "quantite") ?? 1m,
        PrixUnitaire = OdbcCellReader.GetNullableDecimal(reader, "prix_unitaire_ht"),
        MontantHt = OdbcCellReader.GetRequiredDecimal(reader, "montant_ht"),
        MontantTva = OdbcCellReader.GetRequiredDecimal(reader, "montant_tva"),
        TauxTva = OdbcCellReader.GetNullableDecimal(reader, "taux_tva"),
        CodeRegime = OdbcCellReader.GetString(reader, "code_regime"),
    };

    // Isole un document source malformé : MapDocument lève SourceSchemaException (date absente, avoir
    // sans origine résoluble) sur UN document. On le met en quarantaine (journalisé) et on poursuit la
    // fenêtre — sinon l'exception interromprait l'itérateur AVANT l'avancée du filigrane et masquerait
    // tous les documents valides suivants (codex P2). Une erreur de LECTURE (colonne absente → schéma
    // incompatible) ou de connexion (SourceUnavailableException) n'est PAS interceptée ici : elle affecte
    // toute l'extraction (pas un seul document) et remonte donc — halt ou réessai de la fenêtre.
    private PivotDocumentDto? TryMapDocument(DemoErpAInvoice invoice)
    {
        try
        {
            return DemoErpARowMapper.MapDocument(invoice);
        }
        catch (SourceSchemaException ex)
        {
            _log.Warn(
                $"Document source « {invoice.Numero ?? invoice.FactureId} » ignoré (quarantaine) : {ex.Message} "
                + "La fenêtre d'extraction se poursuit ; corrigez la source pour le réintégrer au prochain cycle.");
            return null;
        }
    }
}
