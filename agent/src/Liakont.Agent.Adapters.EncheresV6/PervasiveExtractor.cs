namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Extracteur EncheresV6 en ODBC LECTURE SEULE STRICTE (Magic XPA / Pervasive en production, SQL Server
/// en démo) — ADP02/ADP04. Émet DEUX flux de documents pivot concaténés : les bordereaux ACHETEUR (BA,
/// jambe acheteur de la marge — <see cref="PivotDocumentDto.BuyerFees"/>) puis les bordereaux VENDEUR
/// (BV, jambe vendeur — <see cref="PivotDocumentDto.SellerFees"/>). La plateforme agrège les deux jambes
/// en un seul report SE (F03 §2.4/§2.5). Tenant scopé par <c>No_dossier</c> (1 instance = 1 dossier = 1
/// tenant). Schéma de tables paramétrable (<see cref="EncheresV6Schema"/>). L'émetteur et la nature
/// d'opération sont remplis par la PLATEFORME (FilledByPlatform) — l'agent n'a aucune logique métier
/// (CLAUDE.md n°6) et n'écrit/verrouille rien (R1).
/// </summary>
public sealed class PervasiveExtractor : IExtractor
{
    private const int QueryTimeoutSeconds = 30;

    private const string SourceUnavailableMessage =
        "La source EncheresV6 est momentanément indisponible (connexion ou requête ODBC). Vérifiez que la "
        + "base et le pilote ODBC sont accessibles ; le prochain cycle d'extraction réessaiera automatiquement.";

    private readonly ISourceConnectionFactory _connectionFactory;
    private readonly EncheresV6Schema _schema;
    private readonly string _dossier;
    private readonly IAgentLog _log;

    /// <summary>Crée l'extracteur ODBC EncheresV6.</summary>
    /// <param name="connectionFactory">Fabrique de connexions ODBC (lecture seule, paramétrage tenant).</param>
    /// <param name="schema">Connaissance du schéma (préfixe de tables paramétrable).</param>
    /// <param name="dossier">N° de dossier comptable (filtre tenant : « 1 » judiciaire / « 2 » volontaire).</param>
    /// <param name="log">Journal (mise en quarantaine d'un document source malformé).</param>
    internal PervasiveExtractor(ISourceConnectionFactory connectionFactory, EncheresV6Schema schema, string dossier, IAgentLog log)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(dossier))
        {
            throw new ArgumentException("Le n° de dossier (filtre tenant) est requis.", nameof(dossier));
        }

        _dossier = dossier.Trim();
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // R9 (gate « document finalisé ») : conformité NON présumée pour la source réelle (aucun prédicat de
        // finalisation au WHERE ; statut « comptabilisé » non sourcé). Fail-closed (false) → la garde plateforme
        // différée bloque plutôt que de risquer un brouillon. À confirmer au test ODBC réel.
        Capabilities = new ExtractorCapabilities(
            providesSourceDocuments: false,
            providesUnlinkedDocumentPool: false,
            hasDetailedLines: true,
            hasCreditNoteLink: true,
            exposesPayments: true,
            regimeKeyShape: RegimeKeyShape.Simple,
            emitterIdentitySource: EmitterIdentitySource.FilledByPlatform,
            hasStoredHeaderTotal: true,
            isMutableAfterIssue: false,
            numberUniquenessScope: NumberUniquenessScope.Global,
            extractsOnlyFinalizedDocuments: false);
    }

    /// <inheritdoc />
    public string SourceName => "EncheresV6";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("EncheresV6", "2.0.0-odbc", "EncheresV6 (ODBC lecture seule — Pervasive / SQL Server)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth()
    {
        IDbConnection connection;
        try
        {
            connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage);
        }
        catch (SourceUnavailableException)
        {
            return HealthCheckResult.Unhealthy(
                "Connexion à la source EncheresV6 impossible : vérifiez que le pilote ODBC est installé et que "
                + "la chaîne de connexion (compte lecture seule) du tenant est correcte.");
        }

        using (connection)
        {
            var counts = new List<string>();
            foreach (string table in _schema.ExpectedTables)
            {
                try
                {
                    counts.Add(table + " (" + CountTable(connection, table).ToString(CultureInfo.InvariantCulture) + ")");
                }
                catch (Exception ex) when (ex is DbException || ex is InvalidOperationException)
                {
                    return HealthCheckResult.Unhealthy(
                        "Table source attendue « " + table + " » introuvable ou inaccessible : vérifiez le schéma "
                        + "EncheresV6 (préfixe configuré) et les droits de lecture seule du compte ODBC.");
                }
            }

            return HealthCheckResult.Healthy("Source EncheresV6 (ODBC, lecture seule) accessible — " + string.Join(", ", counts) + ".");
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // Deux flux concaténés (jambe acheteur puis jambe vendeur de la marge), chacun en streaming O(1 doc).
        foreach (PivotDocumentDto document in ExtractBaDocuments(fromInclusiveUtc, toExclusiveUtc))
        {
            yield return document;
        }

        foreach (PivotDocumentDto document in ExtractBvDocuments(fromInclusiveUtc, toExclusiveUtc))
        {
            yield return document;
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, _schema.SelectPaymentsSql, QueryTimeoutSeconds, _dossier, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                var header = new EncheresV6Bordereau { NoBa = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoBa) };
                var ligne = new EncheresV6Ligne
                {
                    NoLignePv = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoLignePv),
                    CodeLigne = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeLigne),
                    Designation = OdbcCellReader.GetString(reader, EncheresV6Schema.ColLibelleLigne),
                    MontantLigne = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMontantLigne) ?? 0d,
                    DateReglement = OdbcCellReader.GetNullableDate(reader, EncheresV6Schema.ColDateReglement),
                    NoRemise = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoRemise),
                };

                yield return EncheresV6RowMapper.MapPayment(header, ligne);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes()
    {
        var regimes = new List<SourceTaxRegimeDto>();
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, _schema.SelectTaxRegimesSql, QueryTimeoutSeconds))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string? code = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeRegime);
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                regimes.Add(new SourceTaxRegimeDto(
                    code!.Trim(),
                    OdbcCellReader.GetString(reader, EncheresV6Schema.ColLibelleAlias),
                    OdbcCellReader.GetInt(reader, EncheresV6Schema.ColRegimeOccurrences)));
            }
        }

        return regimes;
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();

    private static EncheresV6Bordereau ReadBaHeader(IDataReader reader, string noBa) => new EncheresV6Bordereau
    {
        NoBa = noBa,
        BordereauOuAvoir = OdbcCellReader.GetString(reader, EncheresV6Schema.ColBordereauOuAvoir),
        DateVente = OdbcCellReader.GetNullableDate(reader, EncheresV6Schema.ColDateVente) ?? default(DateTime),
        NoBaLettrage = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoBaLettrage),
        Nom = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNom),
        Prenom = OdbcCellReader.GetString(reader, EncheresV6Schema.ColPrenom),
        Societe = OdbcCellReader.GetString(reader, EncheresV6Schema.ColSociete),
        AcheteurSiren = OdbcCellReader.GetString(reader, EncheresV6Schema.ColAcheteurSiren),
        TvaCee = OdbcCellReader.GetString(reader, EncheresV6Schema.ColTvaCee),
        Adresse = OdbcCellReader.GetString(reader, EncheresV6Schema.ColAdresse),
        CodePostal = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodePostal),
        Ville = OdbcCellReader.GetString(reader, EncheresV6Schema.ColVille),
        CodePays = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodePays),
        TotalBordereau = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColTotalBordereau) ?? 0d,
    };

    private static EncheresV6Bordereau? ReadBaOrigin(IDataReader reader, EncheresV6Bordereau bordereau)
    {
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6Schema.PieceAvoir, StringComparison.Ordinal))
        {
            return null;
        }

        string? originNoBa = OdbcCellReader.GetString(reader, EncheresV6Schema.ColOriginNoBa);
        if (string.IsNullOrWhiteSpace(originNoBa))
        {
            return null;
        }

        return new EncheresV6Bordereau
        {
            NoBa = originNoBa,
            DateVente = OdbcCellReader.GetNullableDate(reader, EncheresV6Schema.ColOriginDateVente) ?? default(DateTime),
        };
    }

    private static EncheresV6Ligne ReadBaLine(IDataReader reader, EncheresV6Bordereau bordereau)
    {
        string? devise = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeDevise);
        if (bordereau.CodeDevise is null && !string.IsNullOrWhiteSpace(devise))
        {
            bordereau.CodeDevise = devise;
        }

        return new EncheresV6Ligne
        {
            TypeLigne = OdbcCellReader.GetString(reader, EncheresV6Schema.ColTypeLigne),
            CodeLigne = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeLigne),
            NoLignePv = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoLignePv),
            NoLigneToutPv = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoLigneToutPv),
            Designation = OdbcCellReader.GetString(reader, EncheresV6Schema.ColLibelleLigne),
            MontantAdjHt = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMontantAdjHt) ?? 0d,
            MttTvaInclusAdj = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMttTvaInclusAdj) ?? 0d,
            MttTvaEnPlusAdj = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMttTvaEnPlusAdj) ?? 0d,
            MontantFraisHt = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMontantFraisHt) ?? 0d,
            MontantTvaFrais = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMontantTvaFrais) ?? 0d,
            CodeRegime = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeRegime),
            CodeDevise = devise,
        };
    }

    private static EncheresV6BordereauVendeur ReadBvHeader(IDataReader reader, string noBv) => new EncheresV6BordereauVendeur
    {
        NoBv = noBv,
        BordereauOuAvoir = OdbcCellReader.GetString(reader, EncheresV6Schema.ColBordereauOuAvoir),
        DateVente = OdbcCellReader.GetNullableDate(reader, EncheresV6Schema.ColDateVente) ?? default(DateTime),
        NoBvLettrage = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoBvLettrage),
        Nom = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNom),
        Prenom = OdbcCellReader.GetString(reader, EncheresV6Schema.ColPrenom),
        CodePostal = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodePostal),
        Ville = OdbcCellReader.GetString(reader, EncheresV6Schema.ColVille),
        CodePays = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodePays),
        CodeRegimeTva = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeRegimeTva),
        TotalBordereau = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColTotalBordereau) ?? 0d,
    };

    private static EncheresV6BordereauVendeur? ReadBvOrigin(IDataReader reader, EncheresV6BordereauVendeur bordereau)
    {
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6Schema.PieceAvoir, StringComparison.Ordinal))
        {
            return null;
        }

        string? originNoBv = OdbcCellReader.GetString(reader, EncheresV6Schema.ColOriginNoBv);
        if (string.IsNullOrWhiteSpace(originNoBv))
        {
            return null;
        }

        return new EncheresV6BordereauVendeur
        {
            NoBv = originNoBv,
            DateVente = OdbcCellReader.GetNullableDate(reader, EncheresV6Schema.ColOriginDateVente) ?? default(DateTime),
        };
    }

    private static EncheresV6LigneVendeur ReadBvLine(IDataReader reader, EncheresV6BordereauVendeur bordereau)
    {
        // Propage la devise (portée par la ligne dans SelectBvDocumentsSql) à l'entête — miroir de ReadBaLine.
        // Sans cela, MapBvDocument retomberait toujours sur EUR et un BV en devise étrangère atterrirait dans
        // le mauvais agrégat de marge (grain jour × devise).
        string? devise = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeDevise);
        if (bordereau.CodeDevise is null && !string.IsNullOrWhiteSpace(devise))
        {
            bordereau.CodeDevise = devise;
        }

        return new EncheresV6LigneVendeur
        {
            TypeLigne = OdbcCellReader.GetString(reader, EncheresV6Schema.ColTypeLigne),
            CodeLigne = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeLigne),
            NoLignePv = OdbcCellReader.GetString(reader, EncheresV6Schema.ColNoLignePv),
            Designation = OdbcCellReader.GetString(reader, EncheresV6Schema.ColLibelleLigne),
            MontantAdjHt = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMontantAdjHt) ?? 0d,
            MttFraisHt = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMttFraisHt) ?? 0d,
            MttTvaFrais = OdbcCellReader.GetNullableDouble(reader, EncheresV6Schema.ColMttTvaFrais) ?? 0d,
            CodeDevise = OdbcCellReader.GetString(reader, EncheresV6Schema.ColCodeDevise),
        };
    }

    private static long CountTable(IDbConnection connection, string table)
    {
        string sql = EncheresV6Schema.CountSql(table);
        EncheresV6Schema.EnsureSelectOnly(sql);

        using (IDbCommand command = SourceQuery.CreateSelect(connection, sql, QueryTimeoutSeconds))
        {
            object? result = command.ExecuteScalar();
            return result is null || result == DBNull.Value ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
    }

    private IEnumerable<PivotDocumentDto> ExtractBaDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, _schema.SelectBaDocumentsSql, QueryTimeoutSeconds, _dossier, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            EncheresV6Bordereau? current = null;
            EncheresV6Bordereau? currentOrigin = null;
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string noBa = OdbcCellReader.GetRequiredString(reader, EncheresV6Schema.ColNoBa);
                if (current is null || !string.Equals(current.NoBa, noBa, StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        yield return MapBaLogged(current, currentOrigin);
                    }

                    current = ReadBaHeader(reader, noBa);
                    currentOrigin = ReadBaOrigin(reader, current);
                }

                if (!string.IsNullOrEmpty(OdbcCellReader.GetString(reader, EncheresV6Schema.ColTypeLigne)))
                {
                    current.Lignes.Add(ReadBaLine(reader, current));
                }
            }

            if (current != null)
            {
                yield return MapBaLogged(current, currentOrigin);
            }
        }
    }

    private IEnumerable<PivotDocumentDto> ExtractBvDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, _schema.SelectBvDocumentsSql, QueryTimeoutSeconds, _dossier, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            EncheresV6BordereauVendeur? current = null;
            EncheresV6BordereauVendeur? currentOrigin = null;
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                string noBv = OdbcCellReader.GetRequiredString(reader, EncheresV6Schema.ColNoBv);
                if (current is null || !string.Equals(current.NoBv, noBv, StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        yield return MapBvLogged(current, currentOrigin);
                    }

                    current = ReadBvHeader(reader, noBv);
                    currentOrigin = ReadBvOrigin(reader, current);
                }

                if (!string.IsNullOrEmpty(OdbcCellReader.GetString(reader, EncheresV6Schema.ColTypeLigne)))
                {
                    current.Lignes.Add(ReadBvLine(reader, current));
                }
            }

            if (current != null)
            {
                yield return MapBvLogged(current, currentOrigin);
            }
        }
    }

    private PivotDocumentDto MapBaLogged(EncheresV6Bordereau bordereau, EncheresV6Bordereau? origin)
    {
        try
        {
            return EncheresV6RowMapper.MapBaDocument(bordereau, origin);
        }
        catch (SourceSchemaException ex)
        {
            // FAIL-CLOSED (CLAUDE.md n°3) : un bordereau malformé (avoir orphelin, date absente, montant illisible)
            // est une erreur FATALE. On journalise le no_ba (visibilité opérateur) PUIS on RELANCE : ExtractionCycle
            // avorte et N'AVANCE PAS le filigrane — le document reste ré-extractible, jamais perdu en silence (un
            // avoir non émis SUR-déclarerait). Parité stricte avec le mode fixtures (qui laisse aussi remonter).
            _log.Warn(
                $"Bordereau acheteur « {bordereau.NoBa} » : extraction BLOQUÉE (donnée source non conforme) — "
                + $"{ex.Message} Corrigez la source ; la période sera ré-extraite (filigrane non avancé).");
            throw;
        }
    }

    private PivotDocumentDto MapBvLogged(EncheresV6BordereauVendeur bordereau, EncheresV6BordereauVendeur? origin)
    {
        try
        {
            return EncheresV6RowMapper.MapBvDocument(bordereau, origin);
        }
        catch (SourceSchemaException ex)
        {
            // FAIL-CLOSED (CLAUDE.md n°3), miroir de MapBaLogged : on journalise puis on RELANCE — le cycle avorte,
            // le filigrane n'avance pas, le bordereau vendeur malformé reste ré-extractible (jamais perdu).
            _log.Warn(
                $"Bordereau vendeur « {bordereau.NoBv} » : extraction BLOQUÉE (donnée source non conforme) — "
                + $"{ex.Message} Corrigez la source ; la période sera ré-extraite (filigrane non avancé).");
            throw;
        }
    }
}
