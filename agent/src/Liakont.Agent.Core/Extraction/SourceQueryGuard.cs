namespace Liakont.Agent.Core.Extraction;

using System;

/// <summary>
/// Garde « lecture seule » sur le SQL d'extraction d'un adaptateur (CLAUDE.md n°5, F01-F02 R1).
/// Défense EN PROFONDEUR : le compte ODBC source est déjà restreint à la lecture (db_datareader), mais
/// cette garde refuse au démarrage toute requête qui ne commence pas par <c>SELECT</c>/<c>WITH</c>, pour
/// qu'aucune requête d'écriture ne puisse être introduite par mégarde dans un adaptateur. Les requêtes
/// d'extraction sont des constantes du code (jamais construites depuis une entrée externe).
/// </summary>
public static class SourceQueryGuard
{
    /// <summary>Vérifie que <paramref name="sql"/> est une requête de lecture (commence par SELECT ou WITH).</summary>
    /// <param name="sql">La requête d'extraction à vérifier.</param>
    /// <exception cref="ArgumentException">Si la requête est nulle ou vide.</exception>
    /// <exception cref="InvalidOperationException">Si la requête n'est pas une lecture (lecture seule stricte).</exception>
    public static void EnsureSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("La requête d'extraction est vide.", nameof(sql));
        }

        string trimmed = sql.TrimStart();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Requête d'extraction refusée : une source Liakont est lue en LECTURE SEULE STRICTE "
                + "(CLAUDE.md n°5). Seules les requêtes SELECT/WITH sont autorisées.");
        }
    }
}
