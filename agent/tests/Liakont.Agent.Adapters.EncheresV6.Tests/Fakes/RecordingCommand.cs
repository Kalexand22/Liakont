namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System.Data;

/// <summary>Commande ADO.NET espionnée : enregistre son texte et la méthode d'exécution utilisée.</summary>
internal sealed class RecordingCommand : IDbCommand
{
    private readonly RecordingConnection _connection;

    public RecordingCommand(RecordingConnection connection)
    {
        _connection = connection;
        Parameters = new FakeParameterCollection();
    }

    public string CommandText { get; set; } = string.Empty;

    public int CommandTimeout { get; set; }

    public CommandType CommandType { get; set; }

    public IDbConnection? Connection { get; set; }

    public IDataParameterCollection Parameters { get; }

    public IDbTransaction? Transaction { get; set; }

    public UpdateRowSource UpdatedRowSource { get; set; }

    public void Cancel()
    {
    }

    public IDbDataParameter CreateParameter() => new FakeParameter();

    public int ExecuteNonQuery()
    {
        _connection.RecordNonQuery(CommandText);
        return 0;
    }

    public IDataReader ExecuteReader()
    {
        _connection.RecordExecution(CommandText);
        return _connection.CreateReader();
    }

    public IDataReader ExecuteReader(CommandBehavior behavior) => ExecuteReader();

    public object? ExecuteScalar()
    {
        _connection.RecordExecution(CommandText);
        return _connection.ResolveScalar(CommandText);
    }

    public void Prepare()
    {
    }

    public void Dispose()
    {
    }
}
