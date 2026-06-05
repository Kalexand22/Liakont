namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using Liakont.Agent.Core.Configuration;

/// <summary>
/// Configuration TYPÉE et VALIDÉE de l'adaptateur EncheresV6 (ADP04), dérivée de la section
/// <c>extraction</c> d'<c>agent.json</c> (<see cref="ExtractionConfig"/>). Elle tranche le
/// <see cref="EncheresV6SourceMode"/> SANS ambiguïté à partir de la configuration (jamais par
/// compilation) : une chaîne ODBC ⇒ <see cref="EncheresV6SourceMode.Pervasive"/>, un chemin de fixtures
/// ⇒ <see cref="EncheresV6SourceMode.Fixture"/>. La règle « chaîne ODBC OU chemin fixtures requis » est
/// EXCLUSIVE : déclarer les deux (mode ambigu) ou aucun (pas de source) est refusé avec un message
/// opérateur français (CLAUDE.md n°3 « bloquer plutôt qu'accepter faux », n°12).
/// <para>
/// La chaîne ODBC reste sous sa forme PROTÉGÉE (DPAPI) : elle n'est jamais déchiffrée ici (CLAUDE.md
/// n°10) — le déchiffrement est différé à l'usage, dans <see cref="EncheresV6ExtractorFactory"/>. Aucune
/// donnée client n'est embarquée (CLAUDE.md n°7) : chaîne ODBC et chemin de fixtures sont du
/// PARAMÉTRAGE de tenant.
/// </para>
/// </summary>
public sealed class EncheresV6AdapterConfig
{
    private EncheresV6AdapterConfig(
        EncheresV6SourceMode mode,
        string? odbcConnectionStringProtected,
        string? fixturesPath)
    {
        Mode = mode;
        OdbcConnectionStringProtected = odbcConnectionStringProtected;
        FixturesPath = fixturesPath;
    }

    /// <summary>Mode source effectif, tranché par la configuration (jamais par compilation).</summary>
    public EncheresV6SourceMode Mode { get; }

    /// <summary>Chaîne ODBC PROTÉGÉE (DPAPI) en mode <see cref="EncheresV6SourceMode.Pervasive"/>, sinon <c>null</c>. Jamais déchiffrée ici.</summary>
    public string? OdbcConnectionStringProtected { get; }

    /// <summary>Chemin (fichier ou répertoire) des fixtures JSON en mode <see cref="EncheresV6SourceMode.Fixture"/>, sinon <c>null</c>.</summary>
    public string? FixturesPath { get; }

    /// <summary>
    /// Construit la configuration de l'adaptateur à partir de la section <c>extraction</c> validée.
    /// </summary>
    /// <param name="extraction">Section d'extraction d'<c>agent.json</c> (jamais nulle).</param>
    /// <returns>La configuration typée de l'adaptateur EncheresV6.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="extraction"/> est nul.</exception>
    /// <exception cref="AgentConfigException">
    /// Si ni la chaîne ODBC ni le chemin de fixtures n'est renseigné, ou si les deux le sont (mode ambigu).
    /// </exception>
    public static EncheresV6AdapterConfig FromExtractionConfig(ExtractionConfig extraction)
    {
        if (extraction is null)
        {
            throw new ArgumentNullException(nameof(extraction));
        }

        bool hasOdbc = !string.IsNullOrWhiteSpace(extraction.OdbcConnectionStringProtected);
        bool hasFixtures = !string.IsNullOrWhiteSpace(extraction.FixturesPath);

        if (hasOdbc && hasFixtures)
        {
            throw new AgentConfigException(
                "La configuration de l'adaptateur EncheresV6 déclare À LA FOIS une chaîne ODBC "
                + "(« extraction.odbcConnectionString ») et un chemin de fixtures (« extraction.fixturesPath ») : "
                + "le mode source est ambigu. N'en gardez qu'un seul (ODBC pour la base réelle, fixtures pour le mode dev/démo).");
        }

        if (!hasOdbc && !hasFixtures)
        {
            throw new AgentConfigException(
                "La configuration de l'adaptateur EncheresV6 ne déclare ni chaîne ODBC "
                + "(« extraction.odbcConnectionString ») ni chemin de fixtures (« extraction.fixturesPath ») : "
                + "renseignez l'un des deux (ODBC pour la base réelle, fixtures pour le mode dev/démo).");
        }

        EncheresV6SourceMode mode = hasOdbc ? EncheresV6SourceMode.Pervasive : EncheresV6SourceMode.Fixture;

        return new EncheresV6AdapterConfig(
            mode,
            hasOdbc ? extraction.OdbcConnectionStringProtected : null,
            hasFixtures ? extraction.FixturesPath : null);
    }
}
