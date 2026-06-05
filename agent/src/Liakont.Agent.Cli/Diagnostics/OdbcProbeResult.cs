namespace Liakont.Agent.Cli.Diagnostics;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Résultat d'une sonde de connectivité ODBC en LECTURE SEULE (F12 §2.1, commande <c>test-odbc</c>).
/// Porte la liste des tables détectées et un message opérateur français en cas d'échec.
/// </summary>
internal sealed class OdbcProbeResult
{
    private OdbcProbeResult(bool success, IReadOnlyList<string> tables, string? message)
    {
        Success = success;
        Tables = tables;
        Message = message;
    }

    /// <summary>La connexion a réussi.</summary>
    public bool Success { get; }

    /// <summary>Tables détectées (vide si la connexion a échoué).</summary>
    public IReadOnlyList<string> Tables { get; }

    /// <summary>Message d'échec en français (null si succès).</summary>
    public string? Message { get; }

    /// <summary>Sonde réussie : la base est joignable, les tables sont listées.</summary>
    public static OdbcProbeResult Connected(IReadOnlyList<string> tables) =>
        new OdbcProbeResult(true, new ReadOnlyCollection<string>(new List<string>(tables ?? Array.Empty<string>())), message: null);

    /// <summary>Sonde en échec : message opérateur expliquant la cause et l'action corrective.</summary>
    public static OdbcProbeResult Failed(string message) =>
        new OdbcProbeResult(false, Array.Empty<string>(), message);
}
