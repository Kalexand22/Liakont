namespace Liakont.Agent.Adapters.TestSupport;

using System;
using System.Collections.Generic;
using System.Data;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

// Fichier de fakes de test PARTAGÉ (lié dans plusieurs projets) : regroupe volontairement la pile ADO
// factice (plusieurs petits types) en un seul fichier.
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Pile ADO.NET FACTICE partagée par les tests d'extracteur (DemoErpA/B) : un
/// <see cref="ISourceConnectionFactory"/> qui sert des lignes en mémoire via un <see cref="IDataReader"/>
/// minimal, sans pilote ODBC. Lié (Compile Include) dans chaque projet de test plutôt que dupliqué.
/// Seuls les membres réellement utilisés par <c>SourceQuery</c> / <c>OdbcCellReader</c> sont implémentés ;
/// le reste lève <see cref="NotSupportedException"/> (garde anti-usage involontaire).
/// </summary>
internal sealed class CapturingAgentLog : IAgentLog
{
    public List<string> Warnings { get; } = new List<string>();

    public void Info(string message)
    {
    }

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null)
    {
    }
}

/// <summary>Fabrique de connexions factices servant les <paramref name="rows"/> fournies.</summary>
internal sealed class FakeSourceConnectionFactory : ISourceConnectionFactory
{
    private readonly IReadOnlyList<Dictionary<string, object>> _rows;

    public FakeSourceConnectionFactory(IReadOnlyList<Dictionary<string, object>> rows) => _rows = rows;

    public IDbConnection CreateConnection() => new FakeConnection(_rows);
}

internal sealed class FakeConnection : IDbConnection
{
    private readonly IReadOnlyList<Dictionary<string, object>> _rows;

    public FakeConnection(IReadOnlyList<Dictionary<string, object>> rows) => _rows = rows;

    public string ConnectionString { get; set; } = string.Empty;

    public int ConnectionTimeout => 0;

    public string Database => string.Empty;

    public ConnectionState State => ConnectionState.Open;

    public void Open()
    {
    }

    public void Close()
    {
    }

    public IDbCommand CreateCommand() => new FakeCommand(_rows);

    public void Dispose()
    {
    }

    public IDbTransaction BeginTransaction() => throw new NotSupportedException();

    public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();

    public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
}

internal sealed class FakeCommand : IDbCommand
{
    private readonly IReadOnlyList<Dictionary<string, object>> _rows;

    public FakeCommand(IReadOnlyList<Dictionary<string, object>> rows) => _rows = rows;

    public string CommandText { get; set; } = string.Empty;

    public int CommandTimeout { get; set; }

    public CommandType CommandType { get; set; }

    public IDbConnection? Connection { get; set; }

    public IDataParameterCollection Parameters { get; } = new FakeParameterCollection();

    public IDbTransaction? Transaction { get; set; }

    public UpdateRowSource UpdatedRowSource { get; set; }

    public void Cancel()
    {
    }

    public IDbDataParameter CreateParameter() => new FakeParameter();

    public void Dispose()
    {
    }

    public int ExecuteNonQuery() => throw new NotSupportedException();

    public IDataReader ExecuteReader() => new FakeDataReader(_rows);

    public IDataReader ExecuteReader(CommandBehavior behavior) => new FakeDataReader(_rows);

    public object ExecuteScalar() => throw new NotSupportedException();

    public void Prepare()
    {
    }
}

internal sealed class FakeParameterCollection : List<object>, IDataParameterCollection
{
    public object this[string parameterName]
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public bool Contains(string parameterName) => throw new NotSupportedException();

    public int IndexOf(string parameterName) => throw new NotSupportedException();

    public void RemoveAt(string parameterName) => throw new NotSupportedException();
}

internal sealed class FakeParameter : IDbDataParameter
{
    public byte Precision { get; set; }

    public byte Scale { get; set; }

    public int Size { get; set; }

    public DbType DbType { get; set; }

    public ParameterDirection Direction { get; set; }

    public bool IsNullable => true;

    public string ParameterName { get; set; } = string.Empty;

    public string SourceColumn { get; set; } = string.Empty;

    public DataRowVersion SourceVersion { get; set; }

    public object? Value { get; set; }
}

internal sealed class FakeDataReader : IDataReader
{
    private readonly IReadOnlyList<Dictionary<string, object>> _rows;
    private int _index = -1;

    public FakeDataReader(IReadOnlyList<Dictionary<string, object>> rows) => _rows = rows;

    public int Depth => 0;

    public bool IsClosed { get; private set; }

    public int RecordsAffected => -1;

    public int FieldCount => _rows.Count > 0 ? _rows[0].Count : 0;

    public object this[string name] => _rows[_index][name];

    public object this[int i] => throw new NotSupportedException();

    public bool Read()
    {
        _index++;
        return _index < _rows.Count;
    }

    public void Close() => IsClosed = true;

    public void Dispose() => IsClosed = true;

    public bool NextResult() => false;

    public string GetName(int i) => throw new NotSupportedException();

    public int GetOrdinal(string name) => throw new NotSupportedException();

    public object GetValue(int i) => throw new NotSupportedException();

    public int GetValues(object[] values) => throw new NotSupportedException();

    public bool IsDBNull(int i) => throw new NotSupportedException();

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    public bool GetBoolean(int i) => throw new NotSupportedException();

    public byte GetByte(int i) => throw new NotSupportedException();

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public char GetChar(int i) => throw new NotSupportedException();

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public string GetDataTypeName(int i) => throw new NotSupportedException();

    public DateTime GetDateTime(int i) => throw new NotSupportedException();

    public decimal GetDecimal(int i) => throw new NotSupportedException();

    public double GetDouble(int i) => throw new NotSupportedException();

    public Type GetFieldType(int i) => throw new NotSupportedException();

    public float GetFloat(int i) => throw new NotSupportedException();

    public Guid GetGuid(int i) => throw new NotSupportedException();

    public short GetInt16(int i) => throw new NotSupportedException();

    public int GetInt32(int i) => throw new NotSupportedException();

    public long GetInt64(int i) => throw new NotSupportedException();

    public string GetString(int i) => throw new NotSupportedException();
}

#pragma warning restore SA1649 // File name should match first type name
#pragma warning restore SA1402 // File may only contain a single type
