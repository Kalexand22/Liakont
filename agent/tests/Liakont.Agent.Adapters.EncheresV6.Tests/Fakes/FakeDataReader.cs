namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Data;

/// <summary>
/// Lecteur de données MOCKÉ : rejoue une liste de lignes (colonne → valeur ; null = DBNull). Reproduit
/// le contrat ADO.NET utilisé par le <see cref="PervasiveExtractor"/> : <c>Read()</c> + indexeur par
/// nom. Une colonne inconnue lève <see cref="IndexOutOfRangeException"/> comme un vrai
/// <c>IDataRecord</c>, ce que l'extracteur traduit en <c>SourceSchemaException</c>.
/// </summary>
internal sealed class FakeDataReader : IDataReader
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;
    private int _position = -1;

    public FakeDataReader(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => _rows = rows;

    public int Depth => 0;

    public bool IsClosed { get; private set; }

    public int RecordsAffected => -1;

    public int FieldCount => _position >= 0 && _position < _rows.Count ? _rows[_position].Count : 0;

    public object this[int i] => throw new NotSupportedException("Accès par ordinal non utilisé.");

    public object this[string name]
    {
        get
        {
            IReadOnlyDictionary<string, object?> row = _rows[_position];
            if (!row.TryGetValue(name, out object? value))
            {
                // Reproduit fidèlement le contrat ADO.NET (IDataRecord lève IndexOutOfRangeException pour
                // une colonne inconnue), que le PervasiveExtractor capture pour lever SourceSchemaException.
#pragma warning disable CA2201 // Mimique volontaire de l'exception levée par le vrai IDataRecord.
                throw new IndexOutOfRangeException(name);
#pragma warning restore CA2201
            }

            if (value is Exception injected)
            {
                throw injected;
            }

            return value ?? DBNull.Value;
        }
    }

    public bool Read()
    {
        _position++;
        return _position < _rows.Count;
    }

    public void Close() => IsClosed = true;

    public void Dispose() => IsClosed = true;

    public bool NextResult() => false;

    public DataTable GetSchemaTable() => throw new NotSupportedException();

    public bool IsDBNull(int i) => throw new NotSupportedException();

    public string GetName(int i) => throw new NotSupportedException();

    public int GetOrdinal(string name) => throw new NotSupportedException();

    public object GetValue(int i) => throw new NotSupportedException();

    public int GetValues(object[] values) => throw new NotSupportedException();

    public string GetDataTypeName(int i) => throw new NotSupportedException();

    public Type GetFieldType(int i) => throw new NotSupportedException();

    public bool GetBoolean(int i) => throw new NotSupportedException();

    public byte GetByte(int i) => throw new NotSupportedException();

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public char GetChar(int i) => throw new NotSupportedException();

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public DateTime GetDateTime(int i) => throw new NotSupportedException();

    public decimal GetDecimal(int i) => throw new NotSupportedException();

    public double GetDouble(int i) => throw new NotSupportedException();

    public float GetFloat(int i) => throw new NotSupportedException();

    public Guid GetGuid(int i) => throw new NotSupportedException();

    public short GetInt16(int i) => throw new NotSupportedException();

    public int GetInt32(int i) => throw new NotSupportedException();

    public long GetInt64(int i) => throw new NotSupportedException();

    public string GetString(int i) => throw new NotSupportedException();
}
