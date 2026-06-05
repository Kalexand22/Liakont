namespace Liakont.Agent.Cli.Diagnostics;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;

/// <summary>
/// Sonde de connectivité ODBC en LECTURE SEULE (F12 §2.1, commande <c>test-odbc</c>, CLAUDE.md n°5 :
/// l'agent ne fait aucune écriture sur la base source). Ouvre la connexion avec la chaîne déchiffrée,
/// liste les tables visibles (schéma uniquement, aucune lecture de données) et n'émet jamais d'exception :
/// toute défaillance devient un message opérateur français (pilote manquant, base injoignable…).
/// <para>
/// Quand AGT02 dotera <c>IExtractor</c> d'un <c>CheckHealth</c>, <c>test-odbc</c> pourra déléguer à
/// l'adaptateur configuré ; ce contrôle générique reste utile en amont (la chaîne et le pilote sont
/// valides avant même qu'un adaptateur sache l'exploiter).
/// </para>
/// </summary>
internal static class OdbcProbe
{
    /// <summary>Probe la chaîne ODBC fournie (déjà déchiffrée). Ne lève jamais d'exception.</summary>
    public static OdbcProbeResult Probe(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return OdbcProbeResult.Failed("La chaîne de connexion ODBC est vide.");
        }

        try
        {
            using (var connection = new OdbcConnection(connectionString))
            {
                connection.Open();

                var tables = new List<string>();
                using (DataTable schema = connection.GetSchema("Tables"))
                {
                    foreach (DataRow row in schema.Rows)
                    {
                        // Ne retient que les tables (exclut les vues système) — colonnes standard du schéma ODBC.
                        object tableType = row["TABLE_TYPE"];
                        if (tableType != DBNull.Value && string.Equals(Convert.ToString(tableType, System.Globalization.CultureInfo.InvariantCulture), "TABLE", StringComparison.OrdinalIgnoreCase))
                        {
                            object name = row["TABLE_NAME"];
                            if (name != DBNull.Value)
                            {
                                tables.Add(Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                            }
                        }
                    }
                }

                return OdbcProbeResult.Connected(tables);
            }
        }
        catch (OdbcException ex)
        {
            return OdbcProbeResult.Failed(
                "Connexion ODBC impossible : " + ex.Message +
                " Vérifiez que le pilote ODBC de la source est installé (en 32 bits si le service tourne en 32 bits) et que la chaîne de connexion est correcte.");
        }
        catch (Exception ex)
        {
            // Sonde de diagnostic : toute autre défaillance (chaîne malformée, pilote absent) devient
            // un message opérateur plutôt qu'un plantage du CLI.
            return OdbcProbeResult.Failed("Connexion ODBC impossible : " + ex.Message);
        }
    }
}
