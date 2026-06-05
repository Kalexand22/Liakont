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

/// <summary>
/// Extracteur EncheresV6 RÉEL en ODBC (Magic XPA / Pervasive / Zen), net48/x86 — ADP02. Lit les
/// DOCUMENTS (bordereaux de vente, <c>bordereau_ou_avoir='B'</c>) d'une période par requêtes
/// <see cref="System.Data.Odbc"/> en LECTURE SEULE STRICTE, puis les transforme en pivot par la MÊME
/// classe de mapping que le mode fixtures (<see cref="EncheresV6RowMapper"/>, ADP01) — seule la source
/// des lignes diffère (F01-F02 §4.4).
/// <para>
/// LECTURE SEULE STRICTE (CLAUDE.md n°5, F01-F02 R1), prouvée par les tests (test-espion sur
/// <c>IDbCommand</c>) ET garantie par construction : uniquement des <c>SELECT</c> (garde
/// <see cref="EncheresV6Schema.EnsureSelectOnly"/>), jamais d'<c>INSERT/UPDATE/DELETE</c>, jamais de
/// transaction d'écriture, jamais de verrou explicite, timeouts courts. L'extraction est idempotente
/// (R2) : la même période renvoie les mêmes <see cref="PivotDocumentDto.SourceReference"/>.
/// </para>
/// <para>
/// Portée jusqu'à ADP03 : <see cref="ExtractDocuments"/> (ventes ET avoirs, avec lien
/// <c>no_ba_lettrage</c> → facture d'origine, F07-F08) + <see cref="ExtractPayments"/> (encaissements bruts,
/// lignes type 3, F09) + <see cref="CheckHealth"/>. La liste des régimes (<see cref="ListSourceTaxRegimes"/>)
/// + la configuration arrivent avec ADP04, les PDF avec ADP05 : ces méthodes renvoient une liste vide tant
/// que leur capacité n'est pas déclarée (jamais d'exception — contrat IExtractor R7). Les capacités déclarées
/// (<see cref="Capabilities"/>) grandissent au fil des items : la plateforme ne s'appuie que sur ce qui est
/// réellement honoré.
/// </para>
/// <para>
/// Aucune donnée client n'est embarquée (CLAUDE.md n°7) : le SIREN émetteur vient du paramétrage
/// (<see cref="EncheresV6EmitterIdentity"/>, absent du schéma EncheresV6) ; la chaîne ODBC est fournie
/// par la fabrique (<see cref="IEncheresV6ConnectionFactory"/>). L'adaptateur ne porte que la
/// connaissance du SCHÉMA (un produit).
/// </para>
/// </summary>
public sealed class PervasiveExtractor : IExtractor
{
    // Timeout court (CLAUDE.md / ADP02) : ne jamais bloquer l'agent sur une base verrouillée ou injoignable.
    private const int QueryTimeoutSeconds = 30;

    // Message opérateur d'indisponibilité (réessayable) — SANS la chaîne de connexion ni aucun secret
    // (CLAUDE.md n°10 : ce message est remonté à la plateforme et journalisé). La cause technique reste
    // dans l'innerException (locale uniquement).
    private const string SourceUnavailableMessage =
        "La source EncheresV6 est momentanément indisponible (connexion ou requête ODBC). Vérifiez que la "
        + "base et le pilote ODBC sont accessibles ; le prochain cycle d'extraction réessaiera automatiquement.";

    private readonly IEncheresV6ConnectionFactory _connectionFactory;
    private readonly EncheresV6EmitterIdentity _emitter;
    private readonly OperationCategory _operationCategory;

    /// <summary>Crée l'extracteur ODBC EncheresV6.</summary>
    /// <param name="connectionFactory">Fabrique de connexions ODBC (paramétrage tenant — ADP04).</param>
    /// <param name="emitter">Identité de l'émetteur (SIREN issu du paramétrage tenant — absent de la base).</param>
    /// <param name="operationCategory">Nature d'opération de la source (paramétrage — F01-F02 §7 #3).</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="connectionFactory"/> ou <paramref name="emitter"/> est nul.</exception>
    public PervasiveExtractor(
        IEncheresV6ConnectionFactory connectionFactory,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _operationCategory = operationCategory;
    }

    /// <inheritdoc />
    public string SourceName => "EncheresV6";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; } = new ExtractorCapabilities(
        providesSourceDocuments: false,
        providesUnlinkedDocumentPool: false,
        hasDetailedLines: true,
        hasCreditNoteLink: true,
        exposesPayments: true,
        regimeKeyShape: RegimeKeyShape.Simple,
        emitterIdentitySource: EmitterIdentitySource.FromConfig,
        hasStoredHeaderTotal: true,
        isMutableAfterIssue: false,
        numberUniquenessScope: NumberUniquenessScope.Global);

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("EncheresV6", "1.0.0-odbc", "Magic XPA / Pervasive (ODBC, lecture seule)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth()
    {
        IDbConnection connection;
        try
        {
            connection = OpenConnection();
        }
        catch (SourceUnavailableException)
        {
            return HealthCheckResult.Unhealthy(
                "Connexion à la source EncheresV6 impossible : vérifiez que le pilote ODBC (en 32 bits si le "
                + "service tourne en 32 bits) est installé et que la chaîne de connexion du tenant est correcte.");
        }

        using (connection)
        {
            var counts = new List<string>();
            foreach (string table in EncheresV6Schema.ExpectedTables)
            {
                try
                {
                    long count = CountTable(connection, table);
                    counts.Add(table + " (" + count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                catch (Exception ex) when (ex is DbException || ex is InvalidOperationException)
                {
                    return HealthCheckResult.Unhealthy(
                        "Table source attendue « " + table + " » introuvable ou inaccessible : vérifiez le schéma "
                        + "EncheresV6 et les droits de lecture seule du compte ODBC.");
                }
            }

            return HealthCheckResult.Healthy(
                "Source EncheresV6 (ODBC, lecture seule) accessible — " + string.Join(", ", counts) + ".");
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // Streaming + regroupement par no_ba (R8) : la requête est triée par no_ba puis no_ligne, donc
        // les lignes d'un même bordereau se suivent. On accumule les lignes du bordereau courant et on
        // émet le document dès que le no_ba change — mémoire O(1 document), jamais tout en mémoire.
        // La requête renvoie ventes ('B') ET avoirs ('A', ADP03) ; pour un avoir, l'entête d'origine est
        // rapportée par l'auto-jointure (colonnes origin_*) sur la MÊME ligne — pas de second lecteur.
        using (IDbConnection connection = OpenConnection())
        using (IDbCommand command = CreatePeriodSelect(connection, EncheresV6Schema.SelectDocumentsSql, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = ExecuteReader(command))
        {
            EncheresV6Bordereau? current = null;
            EncheresV6Bordereau? currentOrigin = null;
            while (ReadNext(reader))
            {
                string noBa = ReadRequiredKey(reader, EncheresV6Schema.ColNoBa);
                if (current is null || !string.Equals(current.NoBa, noBa, StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        yield return MapDocument(current, currentOrigin);
                    }

                    current = ReadBordereauHeader(reader, noBa);

                    // Origine d'un avoir : lue UNIQUEMENT pour 'A' (les colonnes origin_* sont NULL pour une
                    // vente ; un lettrage non résolu laisse l'origine nulle → le mapper bloque l'avoir, jamais
                    // deviné — ADR-0004 D3-3, F07-F08 §B.4).
                    currentOrigin = ReadCreditNoteOrigin(reader, current);
                }

                // LEFT JOIN : une vente sans ligne de document (type 4/2) produit une ligne d'entête seule
                // (colonnes ligne NULL). On n'ajoute alors aucune ligne — le document est tout de même émis
                // (lignes vides), comme en mode fixtures : jamais d'omission silencieuse d'une vente
                // (« bloquer plutôt qu'envoyer faux » ; la cohérence lignes↔total est tranchée par la
                // Validation plateforme, pas par l'adaptateur).
                if (!string.IsNullOrEmpty(ReadString(reader, EncheresV6Schema.ColTypeLigne)))
                {
                    current.Lignes.Add(ReadLigne(reader));
                }
            }

            if (current != null)
            {
                yield return MapDocument(current, currentOrigin);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // Encaissements bruts (F09, ADP03) : lignes type 3 sur la période [date_reglement], rattachées à leur
        // bordereau (numéro de pièce) par l'INNER JOIN. Streaming (R8) : un paiement par ligne, mémoire O(1).
        // L'AGRÉGATION jour × taux est faite par la plateforme (PIP03) — l'adaptateur transmet le brut.
        using (IDbConnection connection = OpenConnection())
        using (IDbCommand command = CreatePeriodSelect(connection, EncheresV6Schema.SelectPaymentsSql, fromInclusiveUtc, toExclusiveUtc))
        using (IDataReader reader = ExecuteReader(command))
        {
            while (ReadNext(reader))
            {
                yield return EncheresV6RowMapper.MapPayment(ReadPaymentBordereau(reader), ReadPaymentLigne(reader));
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes()
    {
        // ADP04 (F03/TVA03) : lecture des régimes déclarés (Regime_tva) + occurrences observées dans
        // lignes_ba, en LECTURE SEULE (EncheresV6Schema.SelectTaxRegimesSql). Régimes BRUTS : ni mappés
        // ni interprétés ici (R3, CLAUDE.md n°2) — le mapping F03 et la couverture sont plateforme.
        // Erreurs TYPÉES (R7) comme ExtractDocuments : SourceUnavailableException (réessayable) sur source
        // momentanément injoignable, SourceSchemaException (fatale) sur schéma incompatible. Côté consommateur,
        // ExtractionCycle traite le catalogue de régimes comme BEST-EFFORT : il CAPTURE une SourceUnavailableException
        // passagère (conserve le dernier état, AVANCE quand même le filigrane — les documents déjà extraits sont
        // idempotents) et réessaie au cycle suivant ; seule SourceSchemaException (fatale) se propage et bloque le
        // filigrane. Liste matérialisée (pas de streaming différé) : la connexion est libérée à la sortie.
        var regimes = new List<SourceTaxRegimeDto>();
        using (IDbConnection connection = OpenConnection())
        using (IDbCommand command = CreateSelect(connection, EncheresV6Schema.SelectTaxRegimesSql))
        using (IDataReader reader = ExecuteReader(command))
        {
            while (ReadNext(reader))
            {
                string? code = ReadString(reader, EncheresV6Schema.ColCodeRegime);
                if (string.IsNullOrWhiteSpace(code))
                {
                    // Entrée Regime_tva sans code exploitable : ignorée (jamais bloquée), exactement comme
                    // le mode fixtures (EncheresV6FixtureExtractor.ListSourceTaxRegimes) — un régime sans code
                    // n'alimente ni le mapping F03 ni la couverture TVA03.
                    continue;
                }

                string? label = ReadString(reader, EncheresV6Schema.ColLibelleRegime);
                int occurrences = ReadOccurrences(reader, EncheresV6Schema.ColRegimeOccurrences);
                regimes.Add(new SourceTaxRegimeDto(code!, label, occurrences));
            }
        }

        return regimes;
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        // Capacité PDF non déclarée en ODBC tant qu'ADP05 ne l'a pas livrée : liste vide, jamais d'exception (contrat IExtractor).
        return Array.Empty<SourceAttachment>();
    }

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // Capacité pool non déclarée en ODBC tant qu'ADP05 ne l'a pas livrée : vide, jamais d'exception (contrat IExtractor).
        return Array.Empty<PoolDocument>();
    }

    private static IDbCommand CreateSelect(IDbConnection connection, string sql)
    {
        EncheresV6Schema.EnsureSelectOnly(sql);

        IDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = QueryTimeoutSeconds;
        return command;
    }

    private static IDbCommand CreatePeriodSelect(IDbConnection connection, string sql, DateTime fromInclusive, DateTime toExclusive)
    {
        IDbCommand command = CreateSelect(connection, sql);
        AddParameter(command, fromInclusive);
        AddParameter(command, toExclusive);
        return command;
    }

    private static void AddParameter(IDbCommand command, object value)
    {
        IDbDataParameter parameter = command.CreateParameter();
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static IDataReader ExecuteReader(IDbCommand command)
    {
        try
        {
            return command.ExecuteReader();
        }
        catch (DbException ex)
        {
            throw new SourceUnavailableException(SourceUnavailableMessage, ex);
        }
    }

    private static bool ReadNext(IDataReader reader)
    {
        try
        {
            return reader.Read();
        }
        catch (DbException ex)
        {
            throw new SourceUnavailableException(SourceUnavailableMessage, ex);
        }
    }

    private static long CountTable(IDbConnection connection, string table)
    {
        string sql = EncheresV6Schema.CountSql(table);
        EncheresV6Schema.EnsureSelectOnly(sql);

        using (IDbCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = QueryTimeoutSeconds;
            object? result = command.ExecuteScalar();
            return result is null || result == DBNull.Value
                ? 0L
                : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
    }

    private static object Cell(IDataReader reader, string column)
    {
        try
        {
            return reader[column];
        }
        catch (DbException ex)
        {
            throw new SourceUnavailableException(SourceUnavailableMessage, ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new SourceSchemaException(
                "Colonne source attendue « " + column + " » absente du résultat EncheresV6 : schéma "
                + "incompatible. Vérifiez la requête d'extraction et le schéma de la base.",
                ex);
        }
    }

    private static string? ReadString(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        return value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string ReadRequiredKey(IDataReader reader, string column)
    {
        string? value = ReadString(reader, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                "Champ source obligatoire « " + column + " » absent : un bordereau sans référence ne peut "
                + "être ni regroupé ni rendu idempotent (R2). Vérifiez l'extraction des données source.");
        }

        return value!;
    }

    private static int ReadOccurrences(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            // COUNT ne renvoie pas NULL, mais on reste défensif : 0 occurrence plutôt qu'une exception.
            return 0;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            throw new SourceSchemaException(
                "Décompte d'occurrences illisible pour un régime de TVA (champ « " + column + " ») : schéma "
                + "EncheresV6 incompatible. Vérifiez l'extraction des régimes source.",
                ex);
        }
    }

    private static double ReadRequiredDouble(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            throw new SourceSchemaException(
                "Montant source obligatoire « " + column + " » absent (NULL) : document bloqué, jamais de "
                + "valeur devinée (ADR-0004 D3-3). Vérifiez l'extraction des montants source.");
        }

        return ConvertDouble(value, column);
    }

    private static double? ReadNullableDouble(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        return value == DBNull.Value ? (double?)null : ConvertDouble(value, column);
    }

    private static double ConvertDouble(object value, string column)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            throw new SourceSchemaException(
                "Valeur source illisible pour le champ « " + column + " » (type inattendu) : schéma "
                + "EncheresV6 incompatible. Vérifiez l'extraction des données source.",
                ex);
        }
    }

    private static DateTime? ReadNullableDate(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            return null;
        }

        try
        {
            return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException)
        {
            throw new SourceSchemaException(
                "Date source illisible pour le champ « " + column + " » : schéma EncheresV6 incompatible. "
                + "Vérifiez l'extraction des données source.",
                ex);
        }
    }

    private static EncheresV6Bordereau ReadBordereauHeader(IDataReader reader, string noBa)
    {
        return new EncheresV6Bordereau
        {
            NoBa = noBa,
            NumeroPiece = ReadString(reader, EncheresV6Schema.ColNumeroPiece),
            BordereauOuAvoir = ReadString(reader, EncheresV6Schema.ColBordereauOuAvoir),
            DateVente = ReadNullableDate(reader, EncheresV6Schema.ColDateVente) ?? default(DateTime),
            NoBaLettrage = ReadString(reader, EncheresV6Schema.ColNoBaLettrage),
            AcheteurNom = ReadString(reader, EncheresV6Schema.ColAcheteurNom),
            AcheteurSociete = ReadString(reader, EncheresV6Schema.ColSociete),
            AcheteurSiren = ReadString(reader, EncheresV6Schema.ColAcheteurSiren),
            AcheteurVille = ReadString(reader, EncheresV6Schema.ColAcheteurVille),
            AcheteurCodePostal = ReadString(reader, EncheresV6Schema.ColAcheteurCodePostal),
            AcheteurPays = ReadString(reader, EncheresV6Schema.ColAcheteurPays),
            TotalHt = ReadRequiredDouble(reader, EncheresV6Schema.ColTotalHt),
            TotalTva = ReadRequiredDouble(reader, EncheresV6Schema.ColTotalTva),
            TotalTtc = ReadRequiredDouble(reader, EncheresV6Schema.ColTotalBordereau),
        };
    }

    private static EncheresV6Ligne ReadLigne(IDataReader reader)
    {
        return new EncheresV6Ligne
        {
            TypeLigne = ReadString(reader, EncheresV6Schema.ColTypeLigne),
            Designation = ReadString(reader, EncheresV6Schema.ColDesignation),
            MontantHt = ReadRequiredDouble(reader, EncheresV6Schema.ColMontantHt),
            MontantTva = ReadRequiredDouble(reader, EncheresV6Schema.ColMontantTva),
            TauxTva = ReadNullableDouble(reader, EncheresV6Schema.ColTauxTva),
            Quantite = ReadNullableDouble(reader, EncheresV6Schema.ColQuantite),
            PrixUnitaire = ReadNullableDouble(reader, EncheresV6Schema.ColPrixUnitaire),
            CodeRegime = ReadString(reader, EncheresV6Schema.ColCodeRegime),
            NoLigne = ReadString(reader, EncheresV6Schema.ColNoLigne),
        };
    }

    private static EncheresV6Ligne ReadPaymentLigne(IDataReader reader)
    {
        return new EncheresV6Ligne
        {
            TypeLigne = EncheresV6RowMapper.LigneReglement,
            NoLigne = ReadString(reader, EncheresV6Schema.ColNoLigne),

            // Montant ENCAISSÉ : colonne montant_ligne (F09 §5.1), portée par le champ partagé MontantHt que
            // le mapper lit pour produire PivotPaymentDto.Amount — jamais le montant_ht des lignes de document.
            MontantHt = ReadRequiredDouble(reader, EncheresV6Schema.ColMontantLigne),
            DateReglement = ReadNullableDate(reader, EncheresV6Schema.ColDateReglement),
            ModeReglement = ReadString(reader, EncheresV6Schema.ColModeReglement),
            NoRemise = ReadString(reader, EncheresV6Schema.ColNoRemise),
        };
    }

    private static EncheresV6Bordereau ReadPaymentBordereau(IDataReader reader)
    {
        // Entête minimale rattachée au règlement (INNER JOIN) : juste de quoi renseigner le numéro de pièce
        // d'origine et la référence source de l'encaissement (le mapper n'a pas besoin du reste).
        return new EncheresV6Bordereau
        {
            NoBa = ReadString(reader, EncheresV6Schema.ColNoBa),
            NumeroPiece = ReadString(reader, EncheresV6Schema.ColNumeroPiece),
        };
    }

    private static EncheresV6Bordereau? ReadCreditNoteOrigin(IDataReader reader, EncheresV6Bordereau bordereau)
    {
        // Seuls les avoirs ('A') portent une origine ; pour une vente, les colonnes origin_* peuvent même être
        // absentes du résultat — on ne les lit jamais. Pour un avoir, un lettrage non résolu (origin_no_ba NULL)
        // laisse l'origine nulle : le mapper bloque alors l'avoir (jamais d'origine devinée — ADR-0004 D3-3).
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6Schema.PieceAvoir, StringComparison.Ordinal))
        {
            return null;
        }

        string? originNoBa = ReadString(reader, EncheresV6Schema.ColOriginNoBa);
        if (string.IsNullOrWhiteSpace(originNoBa))
        {
            return null;
        }

        return new EncheresV6Bordereau
        {
            NoBa = originNoBa,
            NumeroPiece = ReadString(reader, EncheresV6Schema.ColOriginNumeroPiece),
            DateVente = ReadNullableDate(reader, EncheresV6Schema.ColOriginDateVente) ?? default(DateTime),
        };
    }

    private IDbConnection OpenConnection()
    {
        IDbConnection connection = _connectionFactory.CreateConnection();
        try
        {
            connection.Open();
        }
        catch (Exception ex) when (ex is DbException || ex is InvalidOperationException)
        {
            connection.Dispose();
            throw new SourceUnavailableException(SourceUnavailableMessage, ex);
        }

        return connection;
    }

    private PivotDocumentDto MapDocument(EncheresV6Bordereau bordereau, EncheresV6Bordereau? creditNoteOrigin)
    {
        // Pour un avoir ('A'), creditNoteOrigin est l'entête d'origine résolue via no_ba_lettrage (lien
        // CreditNoteRef → facture d'origine, F07-F08 §B.2) ; null pour une vente ou un avoir non lettré.
        return EncheresV6RowMapper.MapDocument(bordereau, _emitter, _operationCategory, creditNoteOrigin);
    }
}
