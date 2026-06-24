namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Data;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Connexion ADO.NET ESPIONNE pour prouver la LECTURE SEULE STRICTE du <see cref="PervasiveExtractor"/>
/// (acceptance ADP02 : « le test intercepte TOUTES les commandes émises et échoue si une commande
/// non-SELECT, une transaction d'écriture ou un verrou est émis »). Chaque commande exécutée est
/// enregistrée ; toute tentative d'écriture (ExecuteNonQuery, BeginTransaction) est comptée ou bloquée.
/// Sert aussi de fabrique (<see cref="ISourceConnectionFactory"/>) renvoyant elle-même.
/// </summary>
internal sealed class RecordingConnection : IDbConnection, ISourceConnectionFactory
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _documentRows;
    private readonly Func<string, object?> _scalarResolver;
    private readonly Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? _readerResolver;

    public RecordingConnection(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? documentRows = null,
        Func<string, object?>? scalarResolver = null,
        Exception? openException = null,
        Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? readerResolver = null)
    {
        _documentRows = documentRows ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        _scalarResolver = scalarResolver ?? (_ => 0L);
        _readerResolver = readerResolver;
        OpenException = openException;
    }

    /// <summary>Toutes les commandes créées (pour inspecter le SQL, les paramètres, le timeout).</summary>
    public List<RecordingCommand> Commands { get; } = new List<RecordingCommand>();

    /// <summary>Texte de chaque commande effectivement exécutée (lecteur, scalaire ou non-query).</summary>
    public List<string> ExecutedCommandTexts { get; } = new List<string>();

    /// <summary>Nombre d'appels à ExecuteNonQuery (doit rester 0 : aucune écriture).</summary>
    public int NonQueryExecutions { get; private set; }

    /// <summary>Nombre de transactions ouvertes (doit rester 0 : aucune transaction d'écriture/verrou).</summary>
    public int TransactionsBegun { get; private set; }

    /// <summary>Nombre d'ouvertures de connexion.</summary>
    public int OpenCount { get; private set; }

    /// <summary>Exception à lever sur Open (pour tester la traduction en SourceUnavailableException).</summary>
    public Exception? OpenException { get; }

    public string ConnectionString { get; set; } = string.Empty;

    public int ConnectionTimeout => 0;

    public string Database => string.Empty;

    public ConnectionState State { get; private set; } = ConnectionState.Closed;

    public IDbConnection CreateConnection() => this;

    public void Open()
    {
        OpenCount++;
        if (OpenException != null)
        {
            throw OpenException;
        }

        State = ConnectionState.Open;
    }

    public void Close() => State = ConnectionState.Closed;

    public IDbCommand CreateCommand()
    {
        var command = new RecordingCommand(this);
        Commands.Add(command);
        return command;
    }

    public IDbTransaction BeginTransaction()
    {
        TransactionsBegun++;
        throw new NotSupportedException("Lecture seule stricte : aucune transaction ne doit être ouverte sur la source.");
    }

    public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();

    public void ChangeDatabase(string databaseName) =>
        throw new NotSupportedException("Lecture seule stricte : ChangeDatabase interdit.");

    public void Dispose() => Close();

    // Sans résolveur : un seul jeu de lignes pour toute requête (comportement historique des tests ADP02).
    // Avec résolveur : le jeu de lignes est routé par le texte de la commande (documents, paiements, régimes…).
    internal IDataReader CreateReader(string commandText) =>
        new FakeDataReader(_readerResolver != null ? _readerResolver(commandText) : _documentRows);

    internal object? ResolveScalar(string commandText) => _scalarResolver(commandText);

    internal void RecordExecution(string commandText) => ExecutedCommandTexts.Add(commandText);

    internal void RecordNonQuery(string commandText)
    {
        NonQueryExecutions++;
        ExecutedCommandTexts.Add(commandText);
    }
}
