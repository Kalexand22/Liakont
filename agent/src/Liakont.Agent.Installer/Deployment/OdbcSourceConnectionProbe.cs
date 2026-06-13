namespace Liakont.Agent.Installer.Deployment;

using System.Globalization;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Implémentation de production de <see cref="ISourceConnectionProbe"/> : délègue à la sonde ODBC en
/// LECTURE SEULE d'AGT05 (<see cref="OdbcProbe"/>, CLAUDE.md n°5 — aucune écriture, aucun verrou). Aucune
/// logique dupliquée (F13 §3, CLAUDE.md n°6) : ce type ne fait que traduire le résultat de la sonde en
/// <see cref="SourceTestResult"/> pour le moteur. La chaîne reçue est déjà en clair (saisie au wizard) ;
/// elle n'est jamais persistée par cette sonde.
/// </summary>
internal sealed class OdbcSourceConnectionProbe : ISourceConnectionProbe
{
    /// <inheritdoc />
    public SourceTestResult Test(string odbcConnectionString)
    {
        OdbcProbeResult result = OdbcProbe.Probe(odbcConnectionString);
        if (result.Success)
        {
            string message = string.Format(
                CultureInfo.CurrentCulture,
                "Connexion à la base source réussie : {0} table(s) détectée(s). Aucune écriture, aucun verrou (lecture seule).",
                result.Tables.Count);
            return new SourceTestResult(true, message);
        }

        return new SourceTestResult(false, result.Message ?? "Connexion ODBC impossible.");
    }
}
