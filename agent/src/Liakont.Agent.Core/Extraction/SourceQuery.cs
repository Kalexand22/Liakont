namespace Liakont.Agent.Core.Extraction;

using System;
using System.Data;
using System.Data.Common;

/// <summary>
/// Cycle de vie PARTAGÉ d'une requête d'extraction en LECTURE SEULE (ouverture de connexion, commande
/// <c>SELECT</c> paramétrée gardée, exécution et lecture), avec traduction des erreurs ODBC en exceptions
/// TYPÉES (F01-F02 R7) : <see cref="SourceUnavailableException"/> (réessayable) sur indisponibilité, jamais
/// d'exception nue. Mutualisé par les adaptateurs (DemoErpA/DemoErpB) pour éviter la duplication du
/// plomberie ADO.NET. Aucune logique métier : pose, paramètre, lit (CLAUDE.md n°6, lecture seule n°5).
/// </summary>
public static class SourceQuery
{
    /// <summary>Ouvre une connexion source. Traduit l'échec d'ouverture en <see cref="SourceUnavailableException"/>.</summary>
    /// <param name="factory">La fabrique de connexions (lecture seule).</param>
    /// <param name="unavailableMessage">Message opérateur (français) en cas d'indisponibilité — SANS secret.</param>
    /// <returns>Une connexion OUVERTE (à libérer par l'appelant).</returns>
    public static IDbConnection Open(ISourceConnectionFactory factory, string unavailableMessage)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        IDbConnection connection = factory.CreateConnection();
        try
        {
            connection.Open();
        }
        catch (Exception ex) when (ex is DbException || ex is InvalidOperationException)
        {
            connection.Dispose();
            throw new SourceUnavailableException(unavailableMessage, ex);
        }

        return connection;
    }

    /// <summary>
    /// Crée une commande <c>SELECT</c> paramétrée (bornes positionnelles <c>?</c> dans l'ordre fourni),
    /// après application de la garde de lecture seule (<see cref="SourceQueryGuard.EnsureSelectOnly"/>).
    /// </summary>
    /// <param name="connection">La connexion ouverte.</param>
    /// <param name="sql">La requête SELECT (constante du code, jamais une entrée externe).</param>
    /// <param name="timeoutSeconds">Délai d'expiration de la commande (court — ne jamais bloquer l'agent).</param>
    /// <param name="positionalParameters">Les valeurs des paramètres positionnels, dans l'ordre des <c>?</c>.</param>
    /// <returns>La commande prête à être exécutée.</returns>
    public static IDbCommand CreateSelect(IDbConnection connection, string sql, int timeoutSeconds, params object[] positionalParameters)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        SourceQueryGuard.EnsureSelectOnly(sql);

        IDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = timeoutSeconds;

        if (positionalParameters != null)
        {
            foreach (object value in positionalParameters)
            {
                IDbDataParameter parameter = command.CreateParameter();
                parameter.Value = NormalizeParameterValue(value);
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }

    /// <summary>Exécute une commande en lecteur. Traduit l'échec en <see cref="SourceUnavailableException"/>.</summary>
    /// <param name="command">La commande SELECT.</param>
    /// <param name="unavailableMessage">Message opérateur (français) en cas d'indisponibilité.</param>
    /// <returns>Le lecteur de résultats.</returns>
    public static IDataReader ExecuteReader(IDbCommand command, string unavailableMessage)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        try
        {
            return command.ExecuteReader();
        }
        catch (DbException ex)
        {
            throw new SourceUnavailableException(unavailableMessage, ex);
        }
    }

    /// <summary>Avance le lecteur d'une ligne. Traduit l'échec en <see cref="SourceUnavailableException"/>.</summary>
    /// <param name="reader">Le lecteur de résultats.</param>
    /// <param name="unavailableMessage">Message opérateur (français) en cas d'indisponibilité.</param>
    /// <returns><c>true</c> s'il reste une ligne à lire.</returns>
    public static bool Read(IDataReader reader, string unavailableMessage)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        try
        {
            return reader.Read();
        }
        catch (DbException ex)
        {
            throw new SourceUnavailableException(unavailableMessage, ex);
        }
    }

    /// <summary>
    /// Normalise une valeur de paramètre avant liaison. Tronque les <see cref="DateTime"/> à la SECONDE :
    /// le pilote ODBC SQL Server lie un <see cref="DateTime"/> par défaut avec une échelle de fraction de
    /// seconde réduite, ce qui fait DÉBORDER (erreur ODBC 22008 « fractional second precision exceeds the
    /// scale ») un <see cref="DateTime"/> .NET à précision 100 ns (ex. <see cref="DateTime.UtcNow"/>). Une
    /// borne de fenêtre d'extraction n'a aucun besoin de précision infra-seconde.
    /// </summary>
    /// <param name="value">La valeur de paramètre.</param>
    /// <returns>La valeur normalisée (DateTime tronqué à la seconde, sinon inchangée).</returns>
    internal static object NormalizeParameterValue(object value) =>
        value is DateTime dt
            ? new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), dt.Kind)
            : value;
}
