namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System.Data;

/// <summary>Paramètre ADO.NET mocké (positionnel ODBC) — porte la valeur de borne de période.</summary>
internal sealed class FakeParameter : IDbDataParameter
{
    public DbType DbType { get; set; }

    public ParameterDirection Direction { get; set; }

    public bool IsNullable => true;

    public string ParameterName { get; set; } = string.Empty;

    public string SourceColumn { get; set; } = string.Empty;

    public DataRowVersion SourceVersion { get; set; }

    public object? Value { get; set; }

    public byte Precision { get; set; }

    public byte Scale { get; set; }

    public int Size { get; set; }
}
