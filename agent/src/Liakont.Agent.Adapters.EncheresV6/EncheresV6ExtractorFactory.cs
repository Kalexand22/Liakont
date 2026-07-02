namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.IO;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Security;

/// <summary>
/// Composition root de l'adaptateur EncheresV6 (ADP04) : assemble un <see cref="IExtractor"/> configuré
/// depuis <c>agent.json</c>. Le <see cref="EncheresV6SourceMode"/> est tranché par la config (chaîne ODBC
/// ⇒ Pervasive ; chemin de fixtures ⇒ Fixture), jamais par compilation (CLAUDE.md n°8). La chaîne ODBC
/// n'est déchiffrée (DPAPI) qu'ICI et jamais journalisée (n°10). L'émetteur et la nature d'opération NE
/// sont PAS portés par l'agent (FilledByPlatform, parité DemoErpA) : seuls le n° de dossier (filtre
/// tenant) et le préfixe de schéma viennent de <c>adapterConfig.EncheresV6</c>.
/// </summary>
public static class EncheresV6ExtractorFactory
{
    /// <summary>Nom de l'adaptateur (valeur de <c>extraction.adapter</c> et clé de <c>adapterConfig</c>).</summary>
    public const string AdapterName = "EncheresV6";

    /// <summary>Valeur de <c>gedPdf</c> activant la source PDF « tables GED » (seule source PDF actuelle).</summary>
    public const string GedPdfModeTables = "tables";

    private const string KeyDossier = "dossier";
    private const string KeySchema = "schema";
    private const string KeyGedPdf = "gedPdf";
    private const string KeyGedPdfRoot = "gedPdfRoot";

    /// <summary>Crée un extracteur EncheresV6 configuré pour le cycle de run.</summary>
    /// <param name="config">Configuration de l'agent (chargée depuis agent.json).</param>
    /// <param name="protector">Déchiffreur de secrets (DPAPI).</param>
    /// <param name="log">Journal de l'agent (quarantaine d'un document source malformé).</param>
    /// <returns>L'extracteur prêt à l'emploi.</returns>
    public static IExtractor Create(AgentConfig config, ISecretProtector protector, IAgentLog log)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (protector is null)
        {
            throw new ArgumentNullException(nameof(protector));
        }

        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        EncheresV6AdapterConfig adapterMode = EncheresV6AdapterConfig.FromExtractionConfig(config.Extraction);
        AdapterConfigSection section = config.GetAdapterConfig(AdapterName);
        string? schema = section.GetOptional(KeySchema);

        switch (adapterMode.Mode)
        {
            case EncheresV6SourceMode.Pervasive:
                return CreatePervasive(adapterMode, section, schema, protector, log);

            case EncheresV6SourceMode.Fixture:
                EnsureNoGedPdfConfig(section);
                return CreateFixture(adapterMode);

            default:
                throw new AgentConfigException(
                    $"Mode source EncheresV6 inconnu : « {adapterMode.Mode} ». Vérifiez la configuration de l'adaptateur.");
        }
    }

    private static PervasiveExtractor CreatePervasive(
        EncheresV6AdapterConfig adapterMode,
        AdapterConfigSection section,
        string? schema,
        ISecretProtector protector,
        IAgentLog log)
    {
        // Le n° de dossier (filtre tenant) est OBLIGATOIRE en mode ODBC : une seule instance d'agent
        // alimente UN tenant (1 dossier). Absent ⇒ blocage français (jamais d'extraction cross-tenant).
        string dossier = section.GetRequired(KeyDossier);

        string connectionString;
        try
        {
            connectionString = protector.Unprotect(adapterMode.OdbcConnectionStringProtected!);
        }
        catch (Exception ex)
        {
            throw new AgentConfigException(
                "La chaîne de connexion ODBC EncheresV6 n'est pas déchiffrable sur ce poste : "
                + ex.Message + " Re-chiffrez-la sur CETTE machine avec « liakont-agent-cli encrypt » (DPAPI est lié au poste).");
        }

        bool gedPdfEnabled = ResolveGedPdfEnabled(section);
        var connectionFactory = new OdbcSourceConnectionFactory(connectionString);
        var schemaKnowledge = new EncheresV6Schema(schema, includeGedTables: gedPdfEnabled);
        IEncheresV6PdfSource pdfSource = gedPdfEnabled
            ? new GedTableEncheresV6PdfSource(connectionFactory, schemaKnowledge, dossier, section.GetOptional(KeyGedPdfRoot), log)
            : (IEncheresV6PdfSource)NullEncheresV6PdfSource.Instance;

        return new PervasiveExtractor(
            connectionFactory,
            schemaKnowledge,
            dossier,
            log,
            pdfSource);
    }

    /// <summary>
    /// Tranche l'activation de la source PDF « tables GED » depuis <c>adapterConfig.EncheresV6</c> :
    /// <c>gedPdf = "tables"</c> l'active ; absent = pas de PDF (capacité non déclarée). Une valeur
    /// inconnue ou un <c>gedPdfRoot</c> orphelin sont REFUSÉS (config morte ou faute de frappe = un
    /// opérateur qui croit ses PDF transmis alors qu'ils ne le sont pas — bloquer plutôt qu'ignorer).
    /// </summary>
    private static bool ResolveGedPdfEnabled(AdapterConfigSection section)
    {
        string? gedPdf = section.GetOptional(KeyGedPdf);
        if (gedPdf is null)
        {
            if (section.GetOptional(KeyGedPdfRoot) != null)
            {
                throw new AgentConfigException(
                    "La configuration de l'adaptateur EncheresV6 déclare « gedPdfRoot » sans « gedPdf » : la racine "
                    + "seule n'active aucune source PDF. Ajoutez « \"gedPdf\": \"" + GedPdfModeTables + "\" » "
                    + "(ou retirez « gedPdfRoot »).");
            }

            return false;
        }

        if (!string.Equals(gedPdf.Trim(), GedPdfModeTables, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentConfigException(
                "Valeur inconnue pour « gedPdf » dans la configuration de l'adaptateur EncheresV6 : « " + gedPdf
                + " ». Seule la valeur « " + GedPdfModeTables + " » (PDF référencés par les tables GED de la "
                + "base source) est prise en charge.");
        }

        return true;
    }

    /// <summary>
    /// Le mode fixtures n'a pas d'ODBC : une config PDF GED présente y est une erreur de paramétrage
    /// (jamais ignorée en silence — l'opérateur croirait ses PDF transmis).
    /// </summary>
    private static void EnsureNoGedPdfConfig(AdapterConfigSection section)
    {
        if (section.GetOptional(KeyGedPdf) != null || section.GetOptional(KeyGedPdfRoot) != null)
        {
            throw new AgentConfigException(
                "La configuration de l'adaptateur EncheresV6 déclare une source PDF GED (« gedPdf »/« gedPdfRoot ») "
                + "en mode fixtures : les tables GED exigent le mode ODBC (« extraction.odbcConnectionString »). "
                + "Retirez ces clés ou passez en mode ODBC.");
        }
    }

    private static EncheresV6FixtureExtractor CreateFixture(EncheresV6AdapterConfig adapterMode)
    {
        // Le mode fixtures rejoue un snapshot déjà filtré (dev/démo/tests) : le préfixe de schéma et le
        // n° de dossier ne s'appliquent pas (pas d'ODBC). Un répertoire fusionne tous ses *.json.
        string path = adapterMode.FixturesPath!;
        return Directory.Exists(path)
            ? EncheresV6FixtureExtractor.FromDirectory(path)
            : EncheresV6FixtureExtractor.FromFile(path);
    }
}
