namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests de la source PDF « tables GED » (<see cref="GedTableEncheresV6PdfSource"/>) sur la FORME RÉELLE
/// de la donnée (base de démo reconstruite depuis la vraie GED, 02/07/2026) : liaison
/// <c>GED_Relation.Ref_numerique1</c> (flux 5 = BA / 6 = BV), chemin reconstruit
/// <c>racine\dossier\année\mois(2 ch.)\type\référence\fichier</c>, padding CHAR toléré, et régimes d'échec
/// (fichier absent, type de stockage inconnu, référence inexploitable, composante hors racine) qui
/// produisent un Warning français SANS échec — un bordereau sans PDF reste transmis.
/// </summary>
public sealed class GedTableEncheresV6PdfSourceTests : IDisposable
{
    private readonly string _root;

    public GedTableEncheresV6PdfSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liakont-ged-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Ba_reference_queries_flux_5_and_returns_the_reconstructed_path()
    {
        // Cas réel : BA 100352 → 2\2024\05\BA\100352\BA_100352.pdf (mois SUR 2 CHIFFRES).
        string expected = CreatePdf("2", "2024", "05", "BA", "100352", "BA_100352.pdf");
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { GedRow() }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:100352");

        SourceAttachment attachment = attachments.Should().ContainSingle().Subject;
        attachment.FilePath.Should().Be(expected);
        attachment.FileName.Should().Be("BA_100352.pdf");
        attachment.SourceReference.Should().Be("encheresv6:ba:100352");

        RecordingCommand command = GedCommands(connection).Should().ContainSingle().Subject;
        ParameterValues(command).Should().Equal(EncheresV6Schema.GedFluxBa, 100352, "2");
        command.CommandText.Should().Contain("r." + EncheresV6Schema.ColGedSupprime + " = 0", "les relations supprimées sont exclues");
        command.CommandText.Should().Contain("d." + EncheresV6Schema.ColGedSupprime + " = 0", "les documents supprimés sont exclus");
        command.CommandText.Should().Contain("d." + EncheresV6Schema.ColGedNoDossier + " = ?", "la requête est scopée au dossier (tenant)");

        connection.NonQueryExecutions.Should().Be(0, "lecture seule stricte (CLAUDE.md n°5)");
        connection.TransactionsBegun.Should().Be(0, "aucune transaction/verrou");
    }

    [Fact]
    public void Bv_reference_queries_flux_6_and_warns_when_the_ged_has_no_document()
    {
        var log = new RecordingAgentLog();
        var connection = new RecordingConnection(readerResolver: RouteGed(Array.Empty<IReadOnlyDictionary<string, object?>>()));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:bv:100178");

        attachments.Should().BeEmpty("70 BA réels n'ont pas de PDF en GED : cas légitime, jamais bloquant");
        ParameterValues(GedCommands(connection).Single()).Should().Equal(EncheresV6Schema.GedFluxBv, 100178, "2");
        log.Warnings.Should().ContainSingle().Which.Should().Contain("bordereau vendeur")
            .And.Contain("100178", "le Warning opérateur porte le numéro du document (CLAUDE.md n°12)");
    }

    [Fact]
    public void Root_falls_back_to_the_database_storage_path_when_no_override_and_char_padding_is_trimmed()
    {
        // Sans override, la racine vient de GED_Param_Document.Chemin_stockage — et les colonnes CHAR
        // Pervasive arrivent REMBOURRÉES d'espaces : tout est nettoyé avant reconstruction.
        string expected = CreatePdf("2", "2024", "05", "BA", "100352", "BA_100352.pdf");
        var row = GedRow();
        row["Chemin_stockage"] = _root + "\\   ";
        row["Nom_fichier"] = "BA_100352   ";
        row["Extension_fichier"] = ".pdf   ";
        row["Type_modele"] = "BA   ";
        row["Reference"] = "100352   ";
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { row }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: null);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:100352");

        attachments.Should().ContainSingle().Which.FilePath.Should().Be(expected);
    }

    [Fact]
    public void A_referenced_but_missing_file_warns_and_the_document_goes_without_attachment()
    {
        var log = new RecordingAgentLog();
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { GedRow() }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:100352");

        attachments.Should().BeEmpty();
        log.Warnings.Should().Contain(
            w => w.Contains("introuvable") && w.Contains("gedPdfRoot"),
            "le Warning donne l'action corrective (vérifier la racine GED)");
    }

    [Fact]
    public void An_unknown_storage_type_is_skipped_with_a_warning()
    {
        var log = new RecordingAgentLog();
        var row = GedRow();
        row["Type_stockage"] = "B";
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { row }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:100352");

        attachments.Should().BeEmpty("un mode de stockage non sourcé n'est jamais deviné (CLAUDE.md n°2)");
        log.Warnings.Should().Contain(w => w.Contains("type de stockage"));
    }

    [Fact]
    public void An_unparseable_reference_warns_and_queries_nothing()
    {
        var log = new RecordingAgentLog();
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { GedRow() }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:ABC");

        attachments.Should().BeEmpty();
        connection.ExecutedCommandTexts.Should().BeEmpty("pas de requête pour une référence inexploitable");
        log.Warnings.Should().ContainSingle().Which.Should().Contain("ABC");
    }

    [Fact]
    public void A_document_without_sourced_ged_link_is_silent_and_queries_nothing()
    {
        // Factures client / notes d'honoraires : Ref_numerique1 = 0 sur la donnée réelle (liaison non
        // élucidée) — comportement normal : ni requête, ni Warning.
        var log = new RecordingAgentLog();
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { GedRow() }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        source.GetAttachments("encheresv6:fc:00100007").Should().BeEmpty();
        source.GetAttachments("encheresv6:nh:12").Should().BeEmpty();

        connection.ExecutedCommandTexts.Should().BeEmpty();
        log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void A_path_component_escaping_the_ged_root_is_rejected_with_a_warning()
    {
        // Défense en profondeur : une composante malformée (chemin ABSOLU, « .. ») ne fait JAMAIS lire un
        // fichier hors de la racine GED (Path.Combine abandonne la racine devant une composante absolue).
        var log = new RecordingAgentLog();
        var rooted = GedRow();
        rooted["Nom_fichier"] = "C:\\Windows\\evil";
        var climbing = GedRow();
        climbing["Reference"] = "..\\..\\..\\..\\..";
        var connection = new RecordingConnection(readerResolver: RouteGed(new[] { rooted, climbing }));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        IReadOnlyList<SourceAttachment> attachments = source.GetAttachments("encheresv6:ba:100352");

        attachments.Should().BeEmpty();
        log.Warnings.Should().Contain(w => w.Contains("sort de la racine"));
    }

    [Fact]
    public void An_odbc_failure_propagates_as_source_unavailable_and_is_never_swallowed()
    {
        // Régime d'échec INFRA (≠ anomalie de donnée) : un incident ODBC passager doit faire avorter le
        // cycle (filigrane non avancé, réessai au prochain run) — l'avaler en « liste vide » transformerait
        // l'incident en PDF silencieusement perdu (le document partirait, serait acquitté, sans sa pièce).
        var log = new RecordingAgentLog();
        var connection = new RecordingConnection(openException: new FakeDbException("connexion refusée"));
        GedTableEncheresV6PdfSource source = Source(connection, rootOverride: _root, log: log);

        Action act = () => source.GetAttachments("encheresv6:ba:100352");

        act.Should().Throw<SourceUnavailableException>()
            .Which.Message.Should().Contain("GED", "le message opérateur nomme la GED et l'action (réessai automatique)");
        log.Warnings.Should().BeEmpty("pas de Warning « sans pièce jointe » à la place d'une erreur réessayable");
    }

    [Fact]
    public void Capabilities_declare_linked_documents_and_no_pool()
    {
        var source = Source(new RecordingConnection(), rootOverride: _root);

        source.ProvidesSourceDocuments.Should().BeTrue("la GED lie chaque PDF à son bordereau");
        source.ProvidesUnlinkedDocumentPool.Should().BeFalse("il n'existe pas de vrac GED à réconcilier");
        source.ListPoolDocuments(DateTime.MinValue, DateTime.MaxValue).Should().BeEmpty();
    }

    private static Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RouteGed(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        sql => sql.Contains(EncheresV6Schema.TableGedRelation)
            ? rows
            : Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static List<RecordingCommand> GedCommands(RecordingConnection connection) =>
        connection.Commands.Where(c => c.CommandText.Contains(EncheresV6Schema.TableGedRelation)).ToList();

    private static List<object> ParameterValues(RecordingCommand command) =>
        command.Parameters.Cast<IDbDataParameter>().Select(p => p.Value).ToList();

    private static GedTableEncheresV6PdfSource Source(
        RecordingConnection connection,
        string? rootOverride = null,
        RecordingAgentLog? log = null) =>
        new GedTableEncheresV6PdfSource(
            connection,
            new EncheresV6Schema("enc", includeGedTables: true),
            "2",
            rootOverride,
            log ?? new RecordingAgentLog());

    /// <summary>Ligne GED canonique (forme réelle de la base de démo : BA 100352, dossier 2, mai 2024).</summary>
    private static Dictionary<string, object?> GedRow() => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["No"] = 1380,
        ["Type_modele"] = "BA",
        ["Nom_fichier"] = "BA_100352",
        ["Extension_fichier"] = ".pdf",
        ["Annee"] = 2024,
        ["Mois"] = 5,
        ["No_dossier"] = 2,
        ["Reference"] = "100352",
        ["Type_stockage"] = "D",
        ["Chemin_stockage"] = "\\\\serveur-client\\ged\\",
    };

    private string CreatePdf(params string[] segments)
    {
        string path = _root;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x25, 0x50, 0x44, 0x46 });
        return path;
    }
}
