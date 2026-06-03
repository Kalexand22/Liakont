namespace Stratum.Common.Infrastructure.HealthChecks;

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stratum.Common.Infrastructure.Database;
using Xunit;

public class PostgresHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_Should_ReturnHealthy_When_ConnectionSucceeds()
    {
        var check = new PostgresHealthCheck(new AlwaysConnectedFactory());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnUnhealthy_When_ConnectionThrows()
    {
        var check = new PostgresHealthCheck(new AlwaysFailingFactory());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    private static HealthCheckContext MakeContext()
        => new()
        {
            Registration = new HealthCheckRegistration("postgres", _ => null!, HealthStatus.Unhealthy, []),
        };

    // ── Fakes ──────────────────────────────────────────────────────────────
    private sealed class AlwaysConnectedFactory : ISystemConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbConnection>(new FakeDbConnection());
    }

    private sealed class AlwaysFailingFactory : ISystemConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
            => Task.FromException<IDbConnection>(new InvalidOperationException("Connection refused"));
    }

    /// <summary>
    /// Minimal DbConnection that satisfies Dapper's async ExecuteScalarAsync("SELECT 1") path,
    /// which requires a real System.Data.Common.DbConnection whose command is a DbCommand:
    /// CreateDbCommand() → set CommandText → ExecuteScalarAsync() (defaults to ExecuteScalar()).
    /// </summary>
    private sealed class FakeDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "0.0";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => new FakeDbCommand { Connection = this };
    }

    private sealed class FakeDbCommand : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
            => 0;

        // Dapper's ExecuteScalarAsync<int> resolves through DbCommand.ExecuteScalarAsync,
        // whose default implementation invokes this synchronous override.
        public override object ExecuteScalar()
            => 1;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
            => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            _items.Add(value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                _items.Add(value);
            }
        }

        public override void Clear()
            => _items.Clear();

        public override bool Contains(object value)
            => _items.Contains(value);

        public override bool Contains(string value)
            => false;

        public override void CopyTo(Array array, int index)
            => ((System.Collections.ICollection)_items).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator()
            => _items.GetEnumerator();

        public override int IndexOf(object value)
            => _items.IndexOf(value);

        public override int IndexOf(string parameterName)
            => -1;

        public override void Insert(int index, object value)
            => _items.Insert(index, value);

        public override void Remove(object value)
            => _items.Remove(value);

        public override void RemoveAt(int index)
            => _items.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
        }

        protected override DbParameter GetParameter(int index)
            => (DbParameter)_items[index];

        protected override DbParameter GetParameter(string parameterName)
            => throw new NotSupportedException();

        protected override void SetParameter(int index, DbParameter value)
            => _items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
            => throw new NotSupportedException();
    }
}
